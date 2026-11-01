using System.ComponentModel.DataAnnotations;

namespace BikeHub.Mail.Contracts;

// The same contract is used by HTTP and the bikehub.mail.send.v1 Kafka topic.
public sealed record SendEmailRequest(
    Guid MessageId,
    [Required, EmailAddress, StringLength(254)] string Destination,
    [Required, StringLength(200)] string Subject,
    [Required, StringLength(100_000)] string TextBody,
    DateTimeOffset? ExpiresAt = null);

public enum DeliveryStatus { Queued = 0, Sending = 1, Sent = 2, Failed = 3, Expired = 4, Unknown = 5 }

public sealed record EmailStatus(Guid MessageId, DeliveryStatus Status, int Attempts,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? FailureReason);
