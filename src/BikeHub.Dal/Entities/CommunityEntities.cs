namespace BikeHub.Dal.Entities;

public enum RelationKind { Like = 1, Bookmark = 2, Follow = 3, Membership = 4 }
public enum ReportTarget { Bike = 1, Post = 2, Comment = 3, User = 4, Site = 5 }

public sealed class CommunityPost
{
    public Guid Id { get; set; }
    public Guid BikeId { get; set; }
    public Guid AuthorId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string[] Tags { get; set; } = [];
    public string Category { get; set; } = string.Empty;
    public Guid[] ImageIds { get; set; } = [];
    public Guid? ImageId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsHidden { get; set; }
}

public sealed class CommunityComment
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public Guid AuthorId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsHidden { get; set; }
}

public sealed class CommunityGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Category { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
}

public sealed class CommunityRelation
{
    public Guid UserId { get; set; }
    public Guid TargetId { get; set; }
    public RelationKind Kind { get; set; }
}

public sealed class MediaAsset
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long Bytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class ModerationReport
{
    public Guid Id { get; set; }
    public Guid ReporterId { get; set; }
    public ReportTarget TargetType { get; set; }
    public Guid TargetId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public bool Resolved { get; set; }
    public Guid? ResolvedBy { get; set; }
    public string? Resolution { get; set; }
}
