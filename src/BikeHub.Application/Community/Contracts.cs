using System.ComponentModel.DataAnnotations;

namespace BikeHub.Application.Community;

public record Page<T>(IReadOnlyList<T> Items, int Total, int PageNumber, int PageSize)
{
    public bool HasMore => (long)PageNumber * PageSize < Total;
}

public record UserDto(Guid Id, string Name, string Handle, string City, string Initials,
    string Color, string Bio, int Followers, bool IsFollowing, int BikesCount, int PostsCount);

public record BikeDto(Guid Id, Guid OwnerId, string Name, string Description, string Brand,
    BikeCategory Category, int Year, decimal Mileage, string ImageUrl, string Color, UserDto Owner);

public record PostDto(Guid Id, UserDto Author, BikeDto Bike, string Title, string Content,
    string Category, string ImageUrl, string[] Tags, DateTimeOffset CreatedAt,
    int Likes, int CommentsCount, bool IsLiked, bool IsSaved);

public record CommentDto(Guid Id, UserDto Author, string Text, DateTimeOffset CreatedAt);
public record CommunityDto(Guid Id, string Name, string Description, BikeCategory Category,
    string ImageUrl, int Members, bool IsJoined);
public record OverviewDto(int Riders, int Bikes, int Posts, string[] Tags, BikeDto FeaturedBike);

public sealed record CreateBikeRequest(
    [Required, StringLength(100, MinimumLength = 2)] string Name,
    [Required, StringLength(80)] string Brand,
    [EnumDataType(typeof(BikeCategory))] BikeCategory Category,
    [Range(1950, 2100)] int Year,
    [Range(0, 1000000)] decimal Mileage,
    [StringLength(2000)] string Description);

public sealed record CreatePostRequest(
    Guid BikeId,
    [Required, StringLength(160, MinimumLength = 3)] string Title,
    [Required, StringLength(20000, MinimumLength = 10)] string Content,
    [Required, RegularExpression("^(ride|maintenance|upgrade|story)$")] string Category);

public sealed record CreateCommentRequest([Required, StringLength(2000)] string Text);

public interface IFeedService
{
    Page<PostDto> GetFeed(string? tab, BikeCategory? category, string? query, int page, int pageSize);
    OverviewDto GetOverview();
}

public interface IBikeService
{
    Page<BikeDto> GetBikes(string? query, BikeCategory? category, Guid? ownerId, int page, int pageSize);
    BikeDto? GetBike(Guid id);
    BikeDto CreateBike(CreateBikeRequest request);
    Page<PostDto>? GetHistory(Guid id, int page, int pageSize);
}

public interface IPostService
{
    PostDto? GetPost(Guid id);
    PostDto? CreatePost(CreatePostRequest request);
    PostDto? SetLike(Guid id, bool active);
    PostDto? SetBookmark(Guid id, bool active);
    Page<CommentDto>? GetComments(Guid id, int page, int pageSize);
    CommentDto? AddComment(Guid id, CreateCommentRequest request);
}

public interface IUserService
{
    UserDto GetCurrentUser();
    Task<UserDto?> GetUserAsync(Guid id, CancellationToken cancellationToken = default);
    UserDto? SetFollowing(Guid id, bool active);
}

public interface ICommunityService
{
    IReadOnlyList<CommunityDto> GetCommunities(string? query);
    CommunityDto? SetMembership(Guid id, bool active);
}
