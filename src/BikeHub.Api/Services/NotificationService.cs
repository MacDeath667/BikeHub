using BikeHub.Api.Authentication;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Services;

public sealed record NotificationDto(Guid Id, NotificationKind Kind, Guid ActorId, string ActorName, string ActorNickname,
    string? AvatarUrl, ReportTarget TargetType, Guid TargetId, Guid? ContainerId, DateTimeOffset CreatedAt, bool IsRead);

public sealed class NotificationService(IDbContextFactory<BikeHubDbContext> contexts, ICurrentUser current,
    TimeProvider clock, IConfiguration configuration)
{
    private const string SignInTitle = "Войдите в аккаунт.";
    private const string MissingTitle = "Уведомление не найдено.";
    private Guid Actor => current.Id ?? throw new CommunityProblem(401, SignInTitle);
    private bool Memory => configuration["Community:Store"] == Constants.AuthenticationStores.Memory;

    private IQueryable<CommunityNotification> Visible(BikeHubDbContext db)
    {
        var actor = Actor;
        var users = db.Accounts.Where(a => !a.IsBlocked);
        var bikes = db.Bikes.Where(b => !b.IsDeleted && !b.IsHidden && users.Any(a => a.Id == b.OwnerId));
        var posts = db.Posts.Where(p => !p.IsDeleted && !p.IsHidden && bikes.Any(b => b.Id == p.BikeId) && users.Any(a => a.Id == p.AuthorId));
        var entries = db.GroupEntries.Where(e => !e.IsDeleted && !e.IsHidden && users.Any(a => a.Id == e.AuthorId)
            && db.Communities.Any(g => g.Id == e.GroupId && !g.IsDeleted && !g.IsHidden));
        return db.Notifications.AsNoTracking().Where(n => n.RecipientId == actor && users.Any(a => a.Id == n.ActorId) && (
            n.TargetType == ReportTarget.User && users.Any(a => a.Id == n.TargetId) ||
            n.TargetType == ReportTarget.Post && posts.Any(p => p.Id == n.TargetId) ||
            n.TargetType == ReportTarget.Comment && db.Comments.Any(c => c.Id == n.TargetId && !c.IsDeleted && !c.IsHidden
                && posts.Any(p => p.Id == c.PostId) && users.Any(a => a.Id == c.AuthorId)) ||
            n.TargetType == ReportTarget.GroupEntry && entries.Any(e => e.Id == n.TargetId) ||
            n.TargetType == ReportTarget.GroupReply && db.GroupReplies.Any(r => r.Id == n.TargetId && !r.IsDeleted && !r.IsHidden
                && entries.Any(e => e.Id == r.EntryId) && users.Any(a => a.Id == r.AuthorId))));
    }

    public Page<NotificationDto> List(int page, int pageSize)
    {
        page = Math.Clamp(page, 1, 1000000); pageSize = Math.Clamp(pageSize, 1, 50);
        _ = Actor;
        if (Memory)
        {
            return new([], 0, page, pageSize);
        }
        using var db = contexts.CreateDbContext();
        var visible = Visible(db);
        var query = from n in visible.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Skip((page - 1) * pageSize).Take(pageSize)
                    join a in db.Accounts on n.ActorId equals a.Id
                    orderby n.CreatedAt descending, n.Id descending
                    select new NotificationDto(n.Id, n.Kind, a.Id, a.DisplayName, a.Handle ?? string.Empty,
                        a.AvatarId.HasValue ? Constants.MediaPathPrefix + a.AvatarId.Value.ToString() : null,
                        n.TargetType, n.TargetId, n.ContainerId, n.CreatedAt, n.ReadAt.HasValue);
        return new(query.ToArray(), visible.Count(), page, pageSize);
    }

    public int UnreadCount()
    {
        _ = Actor;
        if (Memory)
        {
            return 0;
        }
        using var db = contexts.CreateDbContext();
        return Visible(db).Count(n => n.ReadAt == null);
    }

    public void Read(Guid? id)
    {
        var actor = Actor;
        if (Memory)
        {
            return;
        }
        using var db = contexts.CreateDbContext();
        var query = db.Notifications.Where(n => n.RecipientId == actor);
        if (id.HasValue)
        {
            query = query.Where(n => n.Id == id);
            if (!query.Any())
            {
                throw new CommunityProblem(404, MissingTitle);
            }
        }
        query.Where(n => n.ReadAt == null).ExecuteUpdate(u => u.SetProperty(n => n.ReadAt, clock.GetUtcNow()));
    }
}
