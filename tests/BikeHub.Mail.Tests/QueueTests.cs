using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BikeHub.Mail.Contracts;
using BikeHub.Mail.Kafka;
using BikeHub.Mail.Persistence;
using BikeHub.Mail.Transports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BikeHub.Mail.Tests;

[Collection("Mail persistence")]
public sealed class QueueTests
{
    private static SendEmailRequest Message() => new(Guid.NewGuid(), "recipient@example.test", "Subject", "private-code-123456", DateTimeOffset.UtcNow.AddMinutes(5));
    private static DeliveryWorker Worker(MailQueue queue, IEmailTransport transport, TimeProvider clock) =>
        new(queue, transport, clock, Options.Create(new MailOptions()), NullLogger<DeliveryWorker>.Instance);

    [Fact]
    public async Task Parallel_submissions_and_workers_send_once_and_retain_only_fingerprint()
    {
        await using var database = await MailTestDatabase.CreateAsync();
        var clock = new TestClock();
        var queue = new MailQueue(database, clock);
        var message = Message();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => queue.EnqueueAsync(message, default)));
        Assert.Single(results, x => x.Created);
        var transport = new RecordingTransport();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Worker(queue, transport, clock).ProcessOneAsync(default)));
        Assert.Equal(1, transport.Count);
        Assert.Equal(DeliveryStatus.Sent, (await queue.FindAsync(message.MessageId, default))!.Status);
        var restarted = new MailQueue(database, clock);
        Assert.False((await restarted.EnqueueAsync(message, default)).Created);
        Assert.False(await Worker(restarted, transport, clock).ProcessOneAsync(default));
        await using var context = database.CreateDbContext();
        var stored = await context.Messages.SingleAsync();
        Assert.Null(stored.TextBody);
        Assert.Null(stored.Destination);
        Assert.Equal(64, stored.Fingerprint.Length);
        Assert.False(context.Database.HasPendingModelChanges());
        await context.Database.MigrateAsync();
    }

    [Fact]
    public async Task Http_and_kafka_share_idempotency_and_conflicting_payload_is_rejected()
    {
        await using var database = await MailTestDatabase.CreateAsync();
        await using var app = new MailFactory(database.ConnectionString);
        using var client = app.Client();
        var message = Message();
        using var first = await client.PostAsJsonAsync("/api/emails", message);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var ingress = new KafkaIngress(new MailQueue(database, new TestClock()));
        await ingress.HandleAsync("topic", 0, 1, JsonSerializer.Serialize(message), default);
        using var replay = await client.PostAsJsonAsync("/api/emails", message);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        using var conflict = await client.PostAsJsonAsync("/api/emails", message with { TextBody = "changed" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await ingress.HandleAsync("topic", 0, 2, JsonSerializer.Serialize(message with { TextBody = "changed" }), default);
        await using var context = database.CreateDbContext();
        Assert.Single(await context.Messages.ToListAsync());
        Assert.Equal("idempotency_conflict", (await context.KafkaRejections.SingleAsync()).Reason);
        var response = await client.GetStringAsync("/api/emails/" + message.MessageId);
        Assert.DoesNotContain(message.TextBody, response);
        Assert.DoesNotContain(message.Destination, response);
    }

    [Fact]
    public async Task Http_requires_api_key_and_rejects_mismatched_key_or_invalid_payload()
    {
        await using var database = await MailTestDatabase.CreateAsync();
        await using var app = new MailFactory(database.ConnectionString);
        using var stranger = app.Client(false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.PostAsJsonAsync("/api/emails", Message())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync("/api/emails/" + Guid.NewGuid())).StatusCode);
        using var client = app.Client();
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/emails", Message())).StatusCode);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/emails", Message() with { MessageId = Guid.Empty })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/emails", Message() with { Subject = "Header\r\ninjection" })).StatusCode);
        await using var context = database.CreateDbContext();
        Assert.Empty(await context.Messages.ToListAsync());
    }

    [Fact]
    public async Task Poison_records_are_journalled_without_body_and_do_not_block_next_message()
    {
        await using var database = await MailTestDatabase.CreateAsync();
        var queue = new MailQueue(database, new TestClock());
        var ingress = new KafkaIngress(queue);
        await ingress.HandleAsync("topic", 0, 1, "invalid private-code-123456", default);
        await ingress.HandleAsync("topic", 0, 1, "invalid private-code-123456", default);
        await ingress.HandleAsync("topic", 0, 2, JsonSerializer.Serialize(Message()), default);
        await using var context = database.CreateDbContext();
        Assert.Equal("invalid_payload", (await context.KafkaRejections.SingleAsync()).Reason);
        Assert.Single(await context.Messages.ToListAsync());
    }

    [Fact]
    public async Task Expired_messages_are_not_sent()
    {
        await using var database = await MailTestDatabase.CreateAsync();
        var clock = new TestClock();
        var queue = new MailQueue(database, clock);
        var message = Message();
        await queue.EnqueueAsync(message, default);
        clock.Advance(TimeSpan.FromMinutes(10));
        var transport = new RecordingTransport();
        await Worker(queue, transport, clock).ProcessOneAsync(default);
        Assert.Equal(0, transport.Count);
        Assert.Equal(DeliveryStatus.Expired, (await queue.FindAsync(message.MessageId, default))!.Status);
    }

    [Fact]
    public async Task Failed_connection_is_retried_with_backoff_but_uncertain_send_is_never_retried()
    {
        await using var database = await MailTestDatabase.CreateAsync();
        var clock = new TestClock();
        var queue = new MailQueue(database, clock);
        var message = Message();
        await queue.EnqueueAsync(message, default);
        var transport = new RecordingTransport { Failure = new("connect", "IOException", "connection_error", null, null) };
        var worker = Worker(queue, transport, clock);
        await worker.ProcessOneAsync(default);
        Assert.Equal(DeliveryStatus.Queued, (await queue.FindAsync(message.MessageId, default))!.Status);
        Assert.False(await worker.ProcessOneAsync(default));
        clock.Advance(TimeSpan.FromSeconds(31));
        transport.Failure = new("send", "IOException", "connection_error", null, null);
        await worker.ProcessOneAsync(default);
        Assert.Equal(DeliveryStatus.Unknown, (await queue.FindAsync(message.MessageId, default))!.Status);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.False(await worker.ProcessOneAsync(default));
        Assert.Equal(2, transport.Count);
        Assert.False((await queue.EnqueueAsync(message, default)).Created);
    }

    [Fact]
    public async Task Crash_after_claim_is_recovered_as_unknown_without_sending_again()
    {
        await using var database = await MailTestDatabase.CreateAsync();
        var clock = new TestClock();
        var queue = new MailQueue(database, clock);
        var message = Message();
        await queue.EnqueueAsync(message, default);
        Assert.NotNull(await queue.ClaimAsync(default));
        clock.Advance(TimeSpan.FromMinutes(3));
        var restarted = new MailQueue(database, clock);
        await restarted.RecoverStaleAsync(default);
        Assert.Equal(DeliveryStatus.Unknown, (await restarted.FindAsync(message.MessageId, default))!.Status);
        var transport = new RecordingTransport();
        Assert.False(await Worker(restarted, transport, clock).ProcessOneAsync(default));
        Assert.Equal(0, transport.Count);
    }

    [Fact]
    public async Task Authentication_rejection_is_terminal_and_expired_ingress_does_not_retain_content()
    {
        await using var database = await MailTestDatabase.CreateAsync();
        var clock = new TestClock();
        var queue = new MailQueue(database, clock);
        var expired = Message() with { ExpiresAt = clock.GetUtcNow().AddMinutes(-1) };
        Assert.Equal(DeliveryStatus.Expired, (await queue.EnqueueAsync(expired, default)).Email.Status);
        var message = Message();
        await queue.EnqueueAsync(message, default);
        var transport = new RecordingTransport { Failure = new("authenticate", "AuthenticationException", "auth_rejected", 535, "5.7.8") };
        var worker = Worker(queue, transport, clock);
        await worker.ProcessOneAsync(default);
        Assert.Equal(DeliveryStatus.Failed, (await queue.FindAsync(message.MessageId, default))!.Status);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(await worker.ProcessOneAsync(default));
        await using var context = database.CreateDbContext();
        Assert.All(await context.Messages.ToListAsync(), row => Assert.Null(row.TextBody));
    }

    [Fact]
    public async Task Explicit_smtp_rejection_is_failed_while_temporary_rejection_can_retry()
    {
        await using var database = await MailTestDatabase.CreateAsync();
        var clock = new TestClock();
        var queue = new MailQueue(database, clock);
        var message = Message();
        await queue.EnqueueAsync(message, default);
        var transport = new RecordingTransport { Failure = new("send", "SmtpCommandException", "smtp_rejected", 451, "4.3.0") };
        var worker = Worker(queue, transport, clock);
        await worker.ProcessOneAsync(default);
        Assert.Equal(DeliveryStatus.Queued, (await queue.FindAsync(message.MessageId, default))!.Status);
        clock.Advance(TimeSpan.FromSeconds(31));
        transport.Failure = new("send", "SmtpCommandException", "smtp_rejected", 550, "5.1.1");
        await worker.ProcessOneAsync(default);
        Assert.Equal(DeliveryStatus.Failed, (await queue.FindAsync(message.MessageId, default))!.Status);
    }

    private sealed class RecordingTransport : IEmailTransport
    {
        private int count;
        public int Count => count;
        public EmailTransportException? Failure { get; set; }
        public Task SendAsync(SendEmailRequest message, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref count);
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }
}
