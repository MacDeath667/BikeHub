using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using BikeHub.Mail.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Mail.Persistence;

public sealed class IdempotencyConflictException : Exception;
public sealed record EnqueueResult(EmailStatus Email, bool Created);

public sealed class MailQueue(IDbContextFactory<MailDbContext> contexts, TimeProvider clock)
{
    private const string InvalidMessage = "Invalid email request.";

    public static SendEmailRequest Validate(SendEmailRequest request)
    {
        var results = new List<ValidationResult>();
        if (request.MessageId == Guid.Empty || !Validator.TryValidateObject(request, new(request), results, true) ||
            !MailAddress.TryCreate(request.Destination, out var address) || address.Address != request.Destination ||
            request.Destination.Contains('\r') || request.Destination.Contains('\n') ||
            request.Subject.Contains('\r') || request.Subject.Contains('\n'))
        {
            throw new ValidationException(InvalidMessage);
        }
        return request with { Destination = request.Destination.ToLowerInvariant(), ExpiresAt = request.ExpiresAt?.ToUniversalTime() };
    }

    public async Task<EnqueueResult> EnqueueAsync(SendEmailRequest request, CancellationToken cancellationToken)
    {
        request = Validate(request);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        var now = clock.GetUtcNow();
        var expired = request.ExpiresAt <= now;
        var status = expired ? DeliveryStatus.Expired : DeliveryStatus.Queued;
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        // One INSERT is the arbitration point across HTTP, Kafka, replicas and process restarts.
        var inserted = await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO messages
                (id, fingerprint, destination, subject, text_body, expires_at, status, attempts, created_at, updated_at, not_before)
            VALUES ({request.MessageId}, {fingerprint}, {(expired ? null : request.Destination)},
                {(expired ? null : request.Subject)}, {(expired ? null : request.TextBody)}, {request.ExpiresAt},
                {(int)status}, 0, {now}, {now}, {now})
            ON CONFLICT (id) DO NOTHING
            """, cancellationToken);
        var existing = await context.Messages.AsNoTracking().SingleAsync(x => x.Id == request.MessageId, cancellationToken);
        if (existing.Fingerprint != fingerprint)
        {
            throw new IdempotencyConflictException();
        }
        return new(existing.ToStatus(), inserted == 1);
    }

    public async Task<EmailStatus?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        return (await context.Messages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken))?.ToStatus();
    }

    public async Task<MailMessage?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var messages = await context.Messages.FromSqlInterpolated($"""
            SELECT * FROM messages
            WHERE status = {(int)DeliveryStatus.Queued} AND not_before <= {now}
            ORDER BY created_at, id LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        var message = messages.SingleOrDefault();
        if (message is not null)
        {
            message.Status = DeliveryStatus.Sending;
            message.Attempts++;
            message.UpdatedAt = now;
            await context.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return message;
    }

    public async Task CompleteAsync(Guid id, DeliveryStatus status, string? reason, DateTimeOffset notBefore,
        CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var query = context.Messages.Where(x => x.Id == id && x.Status == DeliveryStatus.Sending);
        if (status == DeliveryStatus.Queued)
        {
            await query.ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, status)
                .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.FailureReason, reason)
                .SetProperty(x => x.NotBefore, notBefore), cancellationToken);
        }
        else
        {
            await query.ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, status)
                .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.FailureReason, reason)
                .SetProperty(x => x.Destination, (string?)null).SetProperty(x => x.Subject, (string?)null)
                .SetProperty(x => x.TextBody, (string?)null), cancellationToken);
        }
    }

    public async Task RecoverStaleAsync(CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var cutoff = now.AddSeconds(-Constants.StaleSendingSeconds);
        await context.Messages.Where(x => x.Status == DeliveryStatus.Sending && x.UpdatedAt < cutoff)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, DeliveryStatus.Unknown)
                .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.FailureReason, "worker_interrupted")
                .SetProperty(x => x.Destination, (string?)null).SetProperty(x => x.Subject, (string?)null)
                .SetProperty(x => x.TextBody, (string?)null), cancellationToken);
    }

    public async Task RejectKafkaAsync(string topic, int partition, long offset, string reason, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow();
        // Do not persist the raw Kafka body, email address or OTP in the rejection journal.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO kafka_rejections (topic, partition, "offset", reason, created_at)
            VALUES ({topic}, {partition}, {offset}, {reason}, {now})
            ON CONFLICT DO NOTHING
            """, cancellationToken);
    }
}
