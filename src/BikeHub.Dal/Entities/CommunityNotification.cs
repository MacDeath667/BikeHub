namespace BikeHub.Dal.Entities;

public enum NotificationKind { Comment = 1, Reply = 2, Follow = 3, Mention = 4 }

public sealed class CommunityNotification
{
    public Guid Id { get; set; }
    public Guid RecipientId { get; set; }
    public Guid ActorId { get; set; }
    public NotificationKind Kind { get; set; }
    public ReportTarget TargetType { get; set; }
    public Guid TargetId { get; set; }
    public Guid? ContainerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
