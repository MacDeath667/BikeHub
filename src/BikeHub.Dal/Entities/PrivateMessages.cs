namespace BikeHub.Dal.Entities;

public enum ConversationStatus { Pending = 1, Accepted = 2, Declined = 3 }

public sealed class PrivateConversation
{
    public Guid Id { get; set; }
    public Guid? FirstUserId { get; set; }
    public Guid? SecondUserId { get; set; }
    public Guid? RequestedById { get; set; }
    public ConversationStatus Status { get; set; }
    public long LatestSequence { get; set; }
    public long FirstReadSequence { get; set; }
    public long SecondReadSequence { get; set; }
    public bool FirstArchived { get; set; }
    public bool SecondArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PrivateMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? SenderId { get; set; }
    public long Sequence { get; set; }
    public Guid ClientMessageId { get; set; }
    public byte[] RequestHash { get; set; } = [];
    public string Text { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public bool IsHidden { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MessageBlock
{
    public Guid UserId { get; set; }
    public Guid BlockedUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PrivateMessageReport
{
    public Guid Id { get; set; }
    public Guid ReporterId { get; set; }
    public Guid? ReportedUserId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? MessageId { get; set; }
    public string MessageText { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedById { get; set; }
    public string? Resolution { get; set; }
}
