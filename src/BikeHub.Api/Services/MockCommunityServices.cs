using BikeHub.Application.Community;

namespace BikeHub.Api.Services;

public sealed class MockFeedService(MockCommunityStore store) : IFeedService
{
    public Page<PostDto> GetFeed(string? tab, string? category, string? query, int page, int pageSize)
    {
        lock (store.Gate)
        {
            IEnumerable<PostDto> posts = store.Posts.Select(store.Hydrate);
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
                store.Hydrate(store.Bikes.First(b => b.Id == Guid.Parse("20000000-0000-0000-0000-000000000001"))));
    }
}

public sealed class MockBikeService(MockCommunityStore store) : IBikeService
{
    public Page<BikeDto> GetBikes(string? query, string? category, Guid? ownerId, int page, int pageSize)
    {
        lock (store.Gate)
        {
            IEnumerable<BikeDto> bikes = store.Bikes.Select(store.Hydrate);
            if (ownerId.HasValue) bikes = bikes.Where(b => b.OwnerId == ownerId);
            if (!string.IsNullOrWhiteSpace(category)) bikes = bikes.Where(b => b.Category == category);
            if (!string.IsNullOrWhiteSpace(query)) bikes = bikes.Where(b => $"{b.Name} {b.Brand} {b.Owner.Name}".Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));
            return MockCommunityStore.Paginate(bikes, page, pageSize);
        }
    }

    public BikeDto? GetBike(Guid id)
    {
        lock (store.Gate) return store.Bikes.Find(b => b.Id == id) is { } bike ? store.Hydrate(bike) : null;
    }

    public BikeDto CreateBike(CreateBikeRequest request)
    {
        lock (store.Gate)
        {
            var image = request.Category switch { "road" => "road", "mtb" => "trail", "city" => "city", _ => "gravel" };
            var bike = new BikeDto(Guid.CreateVersion7(), store.CurrentUserId, request.Name.Trim(), request.Description?.Trim() ?? "",
                request.Brand.Trim(), request.Category, request.Year, request.Mileage, $"/images/{image}.jpg", "", store.Users[0]);
            store.Bikes.Insert(0, bike);
            return store.Hydrate(bike);
        }
    }

    public Page<PostDto>? GetHistory(Guid id, int page, int pageSize)
    {
        lock (store.Gate)
            return store.Bikes.All(b => b.Id != id) ? null : MockCommunityStore.Paginate(
                store.Posts.Where(p => p.Bike.Id == id).OrderByDescending(p => p.CreatedAt).Select(store.Hydrate), page, pageSize);
    }
}

public sealed class MockPostService(MockCommunityStore store) : IPostService
{
    public PostDto? GetPost(Guid id)
    {
        lock (store.Gate) return store.Posts.Find(p => p.Id == id) is { } post ? store.Hydrate(post) : null;
    }

    public PostDto? CreatePost(CreatePostRequest request)
    {
        lock (store.Gate)
        {
            var bike = store.Bikes.Find(b => b.Id == request.BikeId && b.OwnerId == store.CurrentUserId);
            if (bike is null) return null;
            var post = new PostDto(Guid.CreateVersion7(), store.Users[0], bike, request.Title.Trim(), request.Content.Trim(),
                request.Category, bike.ImageUrl, [], DateTimeOffset.UtcNow, 0, 0, false, false);
            store.Posts.Insert(0, post);
            return store.Hydrate(post);
        }
    }

    public PostDto? SetLike(Guid id, bool active) => Update(id, p => p with
    { IsLiked = active, Likes = p.Likes + (p.IsLiked == active ? 0 : active ? 1 : -1) });
    public PostDto? SetBookmark(Guid id, bool active) => Update(id, p => p with { IsSaved = active });

    private PostDto? Update(Guid id, Func<PostDto, PostDto> update)
    {
        lock (store.Gate)
        {
            var index = store.Posts.FindIndex(p => p.Id == id);
            if (index < 0) return null;
            store.Posts[index] = update(store.Posts[index]);
            return store.Hydrate(store.Posts[index]);
        }
    }

    public Page<CommentDto>? GetComments(Guid id, int page, int pageSize)
    {
        lock (store.Gate)
            return store.Posts.All(p => p.Id != id) ? null : MockCommunityStore.Paginate(
                (store.Comments.GetValueOrDefault(id) ?? []).OrderByDescending(c => c.CreatedAt)
                    .Select(c => c with { Author = store.Hydrate(c.Author) }), page, pageSize);
    }

    public CommentDto? AddComment(Guid id, CreateCommentRequest request)
    {
        lock (store.Gate)
        {
            if (store.Posts.All(p => p.Id != id)) return null;
            var comment = new CommentDto(Guid.CreateVersion7(), store.Hydrate(store.Users[0]), request.Text.Trim(), DateTimeOffset.UtcNow);
            if (!store.Comments.ContainsKey(id)) store.Comments[id] = [];
            store.Comments[id].Add(comment);
            return comment;
        }
    }
}

public sealed class MockUserService(MockCommunityStore store) : IUserService
{
    public UserDto GetCurrentUser() { lock (store.Gate) return store.Hydrate(store.Users[0]); }
    public UserDto? GetUser(Guid id)
    {
        lock (store.Gate) return store.Users.Find(u => u.Id == id) is { } user ? store.Hydrate(user) : null;
    }
    public UserDto? SetFollowing(Guid id, bool active)
    {
        lock (store.Gate)
        {
            var index = store.Users.FindIndex(u => u.Id == id && u.Id != store.CurrentUserId);
            if (index < 0) return null;
            var user = store.Users[index];
            store.Users[index] = user with { IsFollowing = active, Followers = user.Followers + (user.IsFollowing == active ? 0 : active ? 1 : -1) };
            return store.Hydrate(store.Users[index]);
        }
    }
}

public sealed class MockCommunityService(MockCommunityStore store) : ICommunityService
{
    public IReadOnlyList<CommunityDto> GetCommunities(string? query)
    {
        lock (store.Gate) return store.Communities.Where(c => string.IsNullOrWhiteSpace(query) ||
            $"{c.Name} {c.Description}".Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
    }
    public CommunityDto? SetMembership(Guid id, bool active)
    {
        lock (store.Gate)
        {
            var index = store.Communities.FindIndex(c => c.Id == id);
            if (index < 0) return null;
            var community = store.Communities[index];
            store.Communities[index] = community with { IsJoined = active, Members = community.Members + (community.IsJoined == active ? 0 : active ? 1 : -1) };
            return store.Communities[index];
        }
    }
}
