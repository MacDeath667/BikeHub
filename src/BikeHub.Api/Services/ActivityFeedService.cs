using BikeHub.Api.Authentication;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Services;

public enum FeedItemKind { Post = 1, GroupEntry = 2 }

public sealed record FeedItemDto(string Id, FeedItemKind Kind, DateTimeOffset CreatedAt,
    PostDto? Post, GroupEntryDto? GroupEntry)
{
    public static FeedItemDto FromPost(PostDto post) => new(PostPrefix + post.Id, FeedItemKind.Post, post.CreatedAt, post, null);
    public static FeedItemDto FromEntry(GroupEntryDto entry) => new(GroupPrefix + entry.Id, FeedItemKind.GroupEntry, entry.CreatedAt, null, entry);
    private const string PostPrefix = "post:";
    private const string GroupPrefix = "group:";
}

public sealed class ActivityFeedService(IDbContextFactory<BikeHubDbContext> contexts,
    PostgresCommunityService community, GroupService groups, IFeedService legacy,
    ICurrentUser current, IConfiguration configuration)
{
    public Page<FeedItemDto> Get(string? tab, BikeCategory? category, string? query, int page, int size)
    {
        page = Math.Clamp(page, 1, 1000000);
        size = Math.Clamp(size, 1, 50);
        if (configuration["Community:Store"] == Constants.AuthenticationStores.Memory || tab == Constants.FeedTabs.Saved)
        {
            var result = legacy.GetFeed(tab, category, query, page, size);
            return new(result.Items.Select(FeedItemDto.FromPost).ToArray(), result.Total, result.PageNumber, result.PageSize);
        }

        using var db = contexts.CreateDbContext();
        var posts = community.Posts(db);
        var entries = groups.Visible(db);
        if (tab == Constants.FeedTabs.Following)
        {
            posts = posts.Where(p => p.Author.IsFollowing);
            entries = entries.Where(e => db.Relations.Any(r => r.UserId == current.Id &&
                r.Kind == RelationKind.Membership && r.TargetId == e.GroupId));
        }
        if (category.HasValue)
        {
            posts = posts.Where(p => p.Bike.Category == category.Value);
            entries = entries.Where(e => db.Communities.Any(g => g.Id == e.GroupId && g.Category == (int)category.Value));
        }
        if (PostgresCommunityService.Search(query) is { } pattern)
        {
            posts = posts.Where(p => EF.Functions.ILike(p.Title + " " + p.Content + " " + p.Bike.Name + " " + p.Author.Name, pattern, Constants.SearchEscapeCharacter));
            entries = entries.Where(e => EF.Functions.ILike(e.Title + " " + e.Content + " " +
                db.Communities.Where(g => g.Id == e.GroupId).Select(g => g.Name).First() + " " +
                db.Accounts.Where(a => a.Id == e.AuthorId).Select(a => a.DisplayName).First(), pattern, Constants.SearchEscapeCharacter));
        }

        // Union only scalar metadata: project collections and authors after selecting the page.
        var timeline = posts.Select(p => new FeedRow { ItemId = p.Id, Kind = FeedItemKind.Post, CreatedAt = p.CreatedAt, Score = p.Likes })
            .Concat(entries.Select(e => new FeedRow { ItemId = e.Id, Kind = FeedItemKind.GroupEntry, CreatedAt = e.CreatedAt,
                Score = db.GroupReplies.Count(r => r.EntryId == e.Id && !r.IsDeleted && !r.IsHidden && db.Accounts.Any(a => a.Id == r.AuthorId && !a.IsBlocked)) }));
        var ordered = tab == Constants.FeedTabs.Popular
            ? timeline.OrderByDescending(x => x.Score).ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Kind).ThenByDescending(x => x.ItemId)
            : timeline.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Kind).ThenByDescending(x => x.ItemId);
        var rows = ordered.Skip((page - 1) * size).Take(size).ToArray();
        var postIds = rows.Where(x => x.Kind == FeedItemKind.Post).Select(x => x.ItemId).ToArray();
        var entryIds = rows.Where(x => x.Kind == FeedItemKind.GroupEntry).Select(x => x.ItemId).ToArray();
        var items = new Dictionary<(FeedItemKind, Guid), FeedItemDto>();
        if (postIds.Length > 0)
        {
            foreach (var post in community.Posts(db).Where(p => postIds.Contains(p.Id)).ToArray())
            {
                items.Add((FeedItemKind.Post, post.Id), FeedItemDto.FromPost(post));
            }
        }
        if (entryIds.Length > 0)
        {
            foreach (var entry in groups.Summaries(db, groups.Visible(db).Where(e => entryIds.Contains(e.Id))))
            {
                items.Add((FeedItemKind.GroupEntry, entry.Id), FeedItemDto.FromEntry(entry));
            }
        }
        return new(rows.Where(row => items.ContainsKey((row.Kind, row.ItemId)))
            .Select(row => items[(row.Kind, row.ItemId)]).ToArray(), timeline.Count(), page, size);
    }

    private sealed class FeedRow
    {
        public Guid ItemId { get; init; }
        public FeedItemKind Kind { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public int Score { get; init; }
    }
}
