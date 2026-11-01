using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using BikeHub.Mail.Contracts;
using BikeHub.Mail.Persistence;

namespace BikeHub.Mail.Kafka;

public sealed class KafkaIngress(MailQueue queue)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string InvalidPayload = "invalid_payload";
    private const string IdempotencyConflict = "idempotency_conflict";
    private const int MaximumPayloadLength = 200_000;

    public async Task HandleAsync(string topic, int partition, long offset, string? payload, CancellationToken cancellationToken)
    {
        string reason;
        try
        {
            var message = payload is not null && payload.Length <= MaximumPayloadLength
                ? JsonSerializer.Deserialize<SendEmailRequest>(payload, JsonOptions) : null;
            if (message is null)
            {
                throw new ValidationException();
            }
            await queue.EnqueueAsync(message, cancellationToken);
            return;
        }
        catch (Exception error) when (error is JsonException or ValidationException or IdempotencyConflictException)
        {
            reason = error is IdempotencyConflictException ? IdempotencyConflict : InvalidPayload;
        }
        // Only poison messages are rejected. A database outage propagates, preventing offset commit.
        await queue.RejectKafkaAsync(topic, partition, offset, reason, cancellationToken);
    }
}
