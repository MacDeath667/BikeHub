using System.ComponentModel.DataAnnotations;
using BikeHub.Api.Authentication;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Services;

public sealed record SaveGroupRequest([Required, StringLength(100, MinimumLength = 2)] string Name,
    [Required, StringLength(2000)] string Description, [EnumDataType(typeof(BikeCategory))] BikeCategory Category, Guid? ImageId);
public sealed record SaveGroupEntryRequest([EnumDataType(typeof(GroupEntryKind))] GroupEntryKind Kind,
    [Required, StringLength(160, MinimumLength = 2)] string Title,
    [Required, StringLength(20000)] string Content, Guid[]? ImageIds, MapRoute? Route = null);
public sealed record GroupEntryDto(Guid Id, Guid GroupId, string GroupName, UserDto Author, GroupEntryKind Kind,
    string Title, string Content, Guid[] ImageIds, DateTimeOffset CreatedAt, int RepliesCount, MapRoute? Route = null, bool HasRoute = false)
{
    public IReadOnlyList<PhotoDto> Photos => ImageIds.Select(id => new PhotoDto(id, BikeHub.Application.Constants.MediaPathPrefix + id)).ToArray();
}

public sealed class GroupService(IDbContextFactory<BikeHubDbContext> contexts, ICurrentUser current,
    PostgresCommunityService community, TimeProvider clock)
{
    private const string Missing = "Материал не найден.";
    private const string Forbidden = "Недостаточно прав для изменения материала.";
    private const string MembershipRequired = "Вступите в сообщество, чтобы участвовать в обсуждении.";
    private Guid Actor => current.Id ?? throw new CommunityProblem(401, "Войдите в аккаунт.");
    private static CommunityGroup Find(BikeHubDbContext db, Guid id) => db.Communities.SingleOrDefault(g => g.Id == id && !g.IsHidden && !g.IsDeleted)
        ?? throw new CommunityProblem(404, Missing);
    private void Member(BikeHubDbContext db, Guid groupId)
    {
        var actor = Actor;
        if (!db.Relations.Any(r => r.UserId == actor && r.TargetId == groupId && r.Kind == RelationKind.Membership))
        {
            throw new CommunityProblem(403, MembershipRequired);
        }
    }
    private void Manage(CommunityGroup group)
    {
        if (group.OwnerId != Actor && !community.IsModerator)
        {
            throw new CommunityProblem(403, Forbidden);
        }
    }
    public CommunityDto Get(Guid id) => community.GetCommunities(null).SingleOrDefault(g => g.Id == id)
        ?? throw new CommunityProblem(404, Missing);
    public Page<UserDto> Members(Guid id, int page, int pageSize)
    {
        using var db = contexts.CreateDbContext();
        Find(db, id);
        page = Math.Clamp(page, 1, 1000000);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var users = community.Users(db).Where(u => db.Relations.Any(r => r.UserId == u.Id && r.TargetId == id && r.Kind == RelationKind.Membership));
        return new(users.OrderBy(u => u.Name).ThenBy(u => u.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
            users.Count(), page, pageSize);
    }
    public CommunityDto Save(Guid? id, SaveGroupRequest request)
    {
        using var db = contexts.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();
        var actor = Actor;
        var group = id.HasValue ? Find(db, id.Value) : new CommunityGroup { Id = Guid.CreateVersion7(), OwnerId = actor };
        if (id.HasValue)
        {
            Manage(group);
        }
        if (request.ImageId != group.ImageId)
        {
            PostgresCommunityService.CheckImages(db, null, request.ImageId, actor);
        }
        group.Name = request.Name.Trim(); group.Description = request.Description.Trim(); group.Category = (int)request.Category;
        group.ImageId = request.ImageId;
        group.ImageUrl = string.Empty;
        if (!id.HasValue)
        {
            db.Communities.Add(group);
            db.Relations.Add(new CommunityRelation { UserId = actor, TargetId = group.Id, Kind = RelationKind.Membership });
        }
        db.SaveChanges(); transaction.Commit();
        return Get(group.Id);
    }
    public void Delete(Guid id)
    {
        using var db = contexts.CreateDbContext();
        var group = Find(db, id); Manage(group);
        using var transaction = db.Database.BeginTransaction();
        group.IsDeleted = true; group.ImageId = null;
        db.GroupEntries.Where(e => e.GroupId == id).ExecuteUpdate(u => u.SetProperty(e => e.IsDeleted, true).SetProperty(e => e.ImageIds, Array.Empty<Guid>()));
        db.SaveChanges(); transaction.Commit();
    }
    private IQueryable<GroupEntry> Visible(BikeHubDbContext db) => db.GroupEntries.AsNoTracking().Where(e => !e.IsDeleted && !e.IsHidden &&
        db.Communities.Any(g => g.Id == e.GroupId && !g.IsDeleted && !g.IsHidden) && db.Accounts.Any(a => a.Id == e.AuthorId && !a.IsBlocked));
    private GroupEntryDto View(BikeHubDbContext db, GroupEntry e, bool includeRoute = true) => new(e.Id, e.GroupId,
        db.Communities.Where(g => g.Id == e.GroupId).Select(g => g.Name).Single(), community.Users(db).Single(u => u.Id == e.AuthorId),
        e.Kind, e.Title, e.Content, e.ImageIds, e.CreatedAt,
        db.GroupReplies.Count(r => r.EntryId == e.Id && !r.IsDeleted && !r.IsHidden && db.Accounts.Any(a => a.Id == r.AuthorId && !a.IsBlocked)), includeRoute ? RouteJson.Read(e.RouteJson) : null, e.RouteJson != null);
    public Page<GroupEntryDto> Entries(Guid groupId, GroupEntryKind kind, int page)
    {
        using var db = contexts.CreateDbContext(); Find(db, groupId);
        page = Math.Clamp(page, 1, 1000000);
        var query = Visible(db).Where(e => e.GroupId == groupId && e.Kind == kind);
        return new(query.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Skip((page - 1) * 20).Take(20)
            .ToList().Select(e => View(db, e, false)).ToArray(), query.Count(), page, 20);
    }
    public GroupEntryDto Entry(Guid id)
    {
        using var db = contexts.CreateDbContext();
        return View(db, Visible(db).SingleOrDefault(e => e.Id == id) ?? throw new CommunityProblem(404, Missing));
    }
    public GroupEntryDto SaveEntry(Guid groupId, Guid? id, SaveGroupEntryRequest request)
    {
        using var db = contexts.CreateDbContext();
        using var transaction = db.Database.BeginTransaction(); Find(db, groupId); Member(db, groupId);
        var actor = Actor;
        var entry = id.HasValue ? db.GroupEntries.SingleOrDefault(e => e.Id == id && e.GroupId == groupId && !e.IsDeleted && !e.IsHidden)
            ?? throw new CommunityProblem(404, Missing) : new GroupEntry { Id = Guid.CreateVersion7(), GroupId = groupId, AuthorId = actor, CreatedAt = clock.GetUtcNow() };
        if (entry.AuthorId != actor)
        {
            throw new CommunityProblem(403, Forbidden);
        }
        entry.RouteJson = RouteService.Save(request.Route);
        entry.Kind = request.Kind; entry.Title = request.Title.Trim(); entry.Content = request.Content.Trim();
        entry.ImageIds = PostgresCommunityService.CheckImages(db, request.ImageIds, null, actor);
        if (id.HasValue)
        {
            entry.UpdatedAt = clock.GetUtcNow();
        }
        else
        {
            db.GroupEntries.Add(entry);
        }
        db.SaveChanges(); transaction.Commit(); return View(db, entry);
    }
    public void DeleteEntry(Guid id)
    {
        using var db = contexts.CreateDbContext();
        var entry = db.GroupEntries.SingleOrDefault(e => e.Id == id && !e.IsDeleted) ?? throw new CommunityProblem(404, Missing);
        if (entry.AuthorId != Actor)
        {
            Manage(Find(db, entry.GroupId));
        }
        entry.IsDeleted = true; entry.ImageIds = []; db.SaveChanges();
    }
    public Page<CommentDto> Replies(Guid id, int page)
    {
        using var db = contexts.CreateDbContext();
        if (!Visible(db).Any(e => e.Id == id))
        {
            throw new CommunityProblem(404, Missing);
        }
        page = Math.Clamp(page, 1, 1000000);
        var query = db.GroupReplies.AsNoTracking().Where(r => r.EntryId == id && !r.IsDeleted && !r.IsHidden && db.Accounts.Any(a => a.Id == r.AuthorId && !a.IsBlocked));
        return new(query.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Skip((page - 1) * 50).Take(50).ToList()
            .Select(r => new CommentDto(r.Id, community.Users(db).Single(a => a.Id == r.AuthorId), r.Text, r.CreatedAt)).ToArray(), query.Count(), page, 50);
    }
    public CommentDto Reply(Guid id, CreateCommentRequest request)
    {
        using var db = contexts.CreateDbContext();
        var entry = Visible(db).SingleOrDefault(e => e.Id == id) ?? throw new CommunityProblem(404, Missing);
        Member(db, entry.GroupId);
        var reply = new GroupReply { Id = Guid.CreateVersion7(), EntryId = id, AuthorId = Actor, Text = request.Text.Trim(), CreatedAt = clock.GetUtcNow() };
        db.GroupReplies.Add(reply); db.SaveChanges();
        return new(reply.Id, community.Users(db).Single(a => a.Id == reply.AuthorId), reply.Text, reply.CreatedAt);
    }
    public void DeleteReply(Guid entryId, Guid id)
    {
        using var db = contexts.CreateDbContext();
        var reply = db.GroupReplies.SingleOrDefault(r => r.Id == id && r.EntryId == entryId && !r.IsDeleted) ?? throw new CommunityProblem(404, Missing);
        if (reply.AuthorId != Actor)
        {
            var groupId = db.GroupEntries.Where(e => e.Id == entryId).Select(e => e.GroupId).Single(); Manage(Find(db, groupId));
        }
        reply.IsDeleted = true; db.SaveChanges();
    }
}
