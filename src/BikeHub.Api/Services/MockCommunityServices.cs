using BikeHub.Api.Authentication;
using BikeHub.Application.Community;

namespace BikeHub.Api.Services;

public sealed class MockFeedService(MockCommunityStore store, ICurrentUser current) : IFeedService
{
    public Page<PostDto> GetFeed(string? tab, string? category, string? query, int page, int pageSize)
    {
        lock (store.Gate)
        {
            IEnumerable<PostDto> posts = store.Posts.Select(p => store.Hydrate(p, current.Id));
            if (tab == "following") posts = posts.Where(p => p.Author.IsFollowing);
            if (tab == "saved") posts = posts.Where(p => p.IsSaved);
            if (!string.IsNullOrWhiteSpace(category)) posts = posts.Where(p => p.Bike.Category == category);
            if (!string.IsNullOrWhiteSpace(query))
                posts = posts.Where(p => $"{p.Title} {p.Content} {p.Bike.Name} {p.Author.Name} {string.Join(' ', p.Tags)}".Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));
            posts = tab == "popular" ? posts.OrderByDescending(p => p.Likes) : posts.OrderByDescending(p => p.CreatedAt);
            return MockCommunityStore.Paginate(posts, page, pageSize);
        }
    }

    public OverviewDto GetOverview()
    {
        lock (store.Gate)
            return new(store.Users.Count, store.Bikes.Count, store.Posts.Count,
                ["гравий", "велопутешествия", "обслуживание", "кастом", "шоссе"],
                store.Hydrate(store.Bikes.First(b => b.Id == Guid.Parse("20000000-0000-0000-0000-000000000001")), current.Id));
    }
}

public sealed class MockBikeService(MockCommunityStore store, ICurrentUser current) : IBikeService
{
    public Page<BikeDto> GetBikes(string? query, string? category, Guid? ownerId, int page, int pageSize)
    {
        lock (store.Gate)
        {
            IEnumerable<BikeDto> bikes = store.Bikes.Select(b => store.Hydrate(b, current.Id));
            if (ownerId.HasValue) bikes = bikes.Where(b => b.OwnerId == ownerId);
            if (!string.IsNullOrWhiteSpace(category)) bikes = bikes.Where(b => b.Category == category);
            if (!string.IsNullOrWhiteSpace(query)) bikes = bikes.Where(b => $"{b.Name} {b.Brand} {b.Owner.Name}".Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));
            return MockCommunityStore.Paginate(bikes, page, pageSize);
        }
    }

    public BikeDto? GetBike(Guid id)
    {
        lock (store.Gate) return store.Bikes.Find(b => b.Id == id) is { } bike ? store.Hydrate(bike, current.Id) : null;
    }

    public BikeDto CreateBike(CreateBikeRequest request)
    {
        var account = current.Account ?? throw new UnauthorizedAccessException();
        lock (store.Gate)
        {
            var owner = store.EnsureUser(account.Id, account.DisplayName);
            var image = request.Category switch { "road" => "road", "mtb" => "trail", "city" => "city", _ => "gravel" };
            var bike = new BikeDto(Guid.CreateVersion7(), owner.Id, request.Name.Trim(), request.Description?.Trim() ?? "",
                request.Brand.Trim(), request.Category, request.Year, request.Mileage, $"/images/{image}.jpg", "", owner);
            store.Bikes.Insert(0, bike);
            return store.Hydrate(bike, current.Id);
        }
    }

    public Page<PostDto>? GetHistory(Guid id, int page, int pageSize)
    {
        lock (store.Gate)
            return store.Bikes.All(b => b.Id != id) ? null : MockCommunityStore.Paginate(
                store.Posts.Where(p => p.Bike.Id == id).OrderByDescending(p => p.CreatedAt).Select(p => store.Hydrate(p, current.Id)), page, pageSize);
    }
}

public sealed class MockPostService(MockCommunityStore store, ICurrentUser current) : IPostService
{
    public PostDto? GetPost(Guid id)
    {
        lock (store.Gate) return store.Posts.Find(p => p.Id == id) is { } post ? store.Hydrate(post, current.Id) : null;
    }

    public PostDto? CreatePost(CreatePostRequest request)
    {
        var account = current.Account ?? throw new UnauthorizedAccessException();
        lock (store.Gate)
        {
            var bike = store.Bikes.Find(b => b.Id == request.BikeId && b.OwnerId == account.Id);
            if (bike is null) return null;
            var author = store.EnsureUser(account.Id, account.DisplayName);
            var post = new PostDto(Guid.CreateVersion7(), author, bike, request.Title.Trim(), request.Content.Trim(),
                request.Category, bike.ImageUrl, [], DateTimeOffset.UtcNow, 0, 0, false, false);
            store.Posts.Insert(0, post);
            return store.Hydrate(post, current.Id);
        }
    }

    public PostDto? SetLike(Guid id, bool active) => SetRelation(store.Likes, id, active);
    public PostDto? SetBookmark(Guid id, bool active) => SetRelation(store.Bookmarks, id, active);

    private PostDto? SetRelation(HashSet<(Guid UserId, Guid TargetId)> relations, Guid id, bool active)
    {
        var actor = current.Id ?? throw new UnauthorizedAccessException();
        lock (store.Gate)
        {
            var post = store.Posts.Find(p => p.Id == id);
            if (post is null) return null;
            if (active) relations.Add((actor, id)); else relations.Remove((actor, id));
            return store.Hydrate(post, actor);
        }
    }

    public Page<CommentDto>? GetComments(Guid id, int page, int pageSize)
    {
        lock (store.Gate)
            return store.Posts.All(p => p.Id != id) ? null : MockCommunityStore.Paginate(
                (store.Comments.GetValueOrDefault(id) ?? []).OrderByDescending(c => c.CreatedAt)
                    .Select(c => c with { Author = store.Hydrate(c.Author, current.Id) }), page, pageSize);
    }

    public CommentDto? AddComment(Guid id, CreateCommentRequest request)
    {
        var account = current.Account ?? throw new UnauthorizedAccessException();
        lock (store.Gate)
        {
            if (store.Posts.All(p => p.Id != id)) return null;
            var comment = new CommentDto(Guid.CreateVersion7(), store.EnsureUser(account.Id, account.DisplayName), request.Text.Trim(), DateTimeOffset.UtcNow);
            if (!store.Comments.ContainsKey(id)) store.Comments[id] = [];
            store.Comments[id].Add(comment);
            return comment;
        }
    }
}

public sealed class MockUserService(MockCommunityStore store, ICurrentUser current, IAuthenticationStore accounts) : IUserService
{
    public UserDto GetCurrentUser()
    {
        var account = current.Account ?? throw new UnauthorizedAccessException();
        return store.EnsureUser(account.Id, account.DisplayName);
    }

    public async Task<UserDto?> GetUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (store.Gate)
            if (store.Users.Find(u => u.Id == id) is { } user) return store.Hydrate(user, current.Id);
        var account = await accounts.GetAccountAsync(id, cancellationToken);
        if (account is null) return null;
        store.EnsureUser(account.Id, account.DisplayName);
        lock (store.Gate) return store.Hydrate(store.Users.First(u => u.Id == id), current.Id);
    }

    public UserDto? SetFollowing(Guid id, bool active)
    {
        var actor = current.Id ?? throw new UnauthorizedAccessException();
        lock (store.Gate)
        {
            var user = store.Users.Find(u => u.Id == id && u.Id != actor);
            if (user is null) return null;
            if (active) store.Following.Add((actor, id)); else store.Following.Remove((actor, id));
            return store.Hydrate(user, actor);
        }
    }
}

public sealed class MockCommunityService(MockCommunityStore store, ICurrentUser current) : ICommunityService
{
    public IReadOnlyList<CommunityDto> GetCommunities(string? query)
    {
        lock (store.Gate) return store.Communities.Where(c => string.IsNullOrWhiteSpace(query) ||
            $"{c.Name} {c.Description}".Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(c => store.Hydrate(c, current.Id)).ToList();
    }
    public CommunityDto? SetMembership(Guid id, bool active)
    {
        var actor = current.Id ?? throw new UnauthorizedAccessException();
        lock (store.Gate)
        {
            var community = store.Communities.Find(c => c.Id == id);
            if (community is null) return null;
            if (active) store.Memberships.Add((actor, id)); else store.Memberships.Remove((actor, id));
            return store.Hydrate(community, actor);
        }
    }
}
