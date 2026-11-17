using System.ComponentModel.DataAnnotations;
using BikeHub.Dal.Entities;

namespace BikeHub.Api.Services;

public sealed record StartConversationRequest(Guid UserId, [Required, StringLength(Constants.Messages.MaximumTextLength)] string Text, Guid ClientMessageId);
public sealed record SendPrivateMessageRequest([Required, StringLength(Constants.Messages.MaximumTextLength)] string Text, Guid ClientMessageId);
public sealed record MessageRequestDecision(bool Accept);
public sealed record ReadConversationRequest([Range(0, long.MaxValue)] long Sequence);
public sealed record ArchiveConversationRequest(bool Archived);
public sealed record PrivateMessageReportRequest([Required, StringLength(2000, MinimumLength = 5)] string Reason);
public sealed record PrivateMessageResolutionRequest([Required, StringLength(2000, MinimumLength = 2)] string Resolution);
public sealed record MessagePeerDto(Guid? Id, string Name, string? Handle, string? AvatarUrl, bool Available);
public sealed record MessageTargetDto(MessagePeerDto Peer, Guid? ConversationId, bool CanRequest, bool BlockedByMe);
public sealed record ConversationDto(Guid Id, MessagePeerDto Peer, ConversationStatus Status, bool RequestedByMe,
    bool CanSend, bool CanAccept, bool BlockedByMe, int UnreadCount, long LatestSequence, DateTimeOffset UpdatedAt, string? LastText);
public sealed record PrivateMessageDto(Guid Id, Guid? SenderId, long Sequence, string? Text, bool IsDeleted, bool IsHidden, DateTimeOffset CreatedAt);
public sealed record MessagePageDto(IReadOnlyList<PrivateMessageDto> Items, bool HasMoreBefore, bool HasMoreAfter, long LatestSequence);
public sealed record PrivateMessageReportDto(Guid Id, Guid? MessageId, MessagePeerDto Reporter, MessagePeerDto ReportedUser,
    string MessageText, string Reason, DateTimeOffset CreatedAt);
