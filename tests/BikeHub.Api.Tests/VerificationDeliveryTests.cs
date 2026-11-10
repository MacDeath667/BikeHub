using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using BikeHub.Api.Authentication;
using BikeHub.Dal;
using BikeHub.Mail.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace BikeHub.Api.Tests;

[Collection("Postgres persistence")]
public sealed class VerificationDeliveryTests
{
    private sealed class MailInbox : IVerificationEmailSender, IVerificationDeliveryStatus, IVerificationEventPublisher
    {
        public bool BrokerAvailable { get; set; }
        public bool HttpFails { get; set; }
        public ConcurrentDictionary<Guid, SendEmailRequest> Messages { get; } = new();
        public ConcurrentQueue<SendEmailRequest> HttpRequests { get; } = new();
        public DeliveryStatus? Status { get; set; }
        public Task PublishAsync(SendEmailRequest message, CancellationToken cancellationToken)
        {
            if (!BrokerAvailable)
            {
                throw new HttpRequestException("broker unavailable");
            }
            Messages.TryAdd(message.MessageId, message);
            return Task.CompletedTask;
        }
        public Task SendAsync(VerificationMessage message, CancellationToken cancellationToken)
        {
            var request = VerificationEmail.Create(message);
            HttpRequests.Enqueue(request);
            if (HttpFails)
            {
                throw new EmailDeliveryException();
            }
            Messages.TryAdd(request.MessageId, request);
            return Task.CompletedTask;
        }
        public Task<DeliveryStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Status);
    }

    private static AuthFactory App(MailInbox inbox, PersistenceTestDatabase? database = null, string? keys = null)
    {
        var app = new AuthFactory { Sender = inbox, Publisher = inbox, Database = database };
        app.Settings["Authentication:EmailProvider"] = "MailService";
        app.Settings["MailService:BaseUrl"] = "http://mail.example.test/";
        app.Settings["MailService:ApiKey"] = new string('M', 32);
        if (database is not null)
        {
            app.Settings["Authentication:Store"] = "Postgres";
        }
        if (keys is not null)
        {
            app.Settings["Authentication:DataProtectionPath"] = keys;
        }
        return app;
    }

    private static async Task<EmailCodeResponse> Request(HttpClient browser)
    {
        using var result = await AuthTestClient.Post(browser, "/api/auth/registration/email/challenges",
            new { email = "delivery@example.test", name = "Rider" });
        Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
        return (await result.Content.ReadFromJsonAsync<EmailCodeResponse>())!;
    }

    [Fact]
    public async Task Kafka_failure_can_fall_back_to_http_after_one_minute_with_the_same_id_code_expiry_and_attempt_budget()
    {
        var inbox = new MailInbox();
        await using var app = App(inbox);
        using var browser = app.Browser();
        using var foreign = app.Browser();
        var challenge = await Request(browser);
        var store = app.Services.GetRequiredService<IAuthenticationStore>();
        var row = (await store.FindChallengeAsync(challenge.ChallengeId, default))!;
        var message = app.Services.GetRequiredService<VerificationPayload>().Read(row);
        Assert.DoesNotContain(message.Destination, row.ProtectedMessage!);
        Assert.Empty(inbox.HttpRequests); // Initial request never calls HTTP.
        await app.Services.GetRequiredService<VerificationOutbox>().DispatchAsync(default);
        Assert.Empty(inbox.Messages);
        using var early = await AuthTestClient.Post(browser, $"/api/auth/email/challenges/{challenge.ChallengeId}/resend");
        Assert.Equal(HttpStatusCode.TooManyRequests, early.StatusCode);
        app.Clock.Advance(TimeSpan.FromSeconds(61));
        using var stolen = await AuthTestClient.Post(foreign, $"/api/auth/email/challenges/{challenge.ChallengeId}/resend");
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
        using var noCsrf = await AuthTestClient.Post(browser, $"/api/auth/email/challenges/{challenge.ChallengeId}/resend", csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var wrong = await AuthTestClient.Post(browser, "/api/auth/email/verify", new { challengeId = challenge.ChallengeId, code = message.Code == "000000" ? "111111" : "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        using var resent = await AuthTestClient.Post(browser, $"/api/auth/email/challenges/{challenge.ChallengeId}/resend");
        Assert.Equal(HttpStatusCode.Accepted, resent.StatusCode);
        var response = (await resent.Content.ReadFromJsonAsync<EmailCodeResponse>())!;
        Assert.Equal(challenge.ChallengeId, response.ChallengeId);
        Assert.Equal(challenge.ExpiresAt, response.ExpiresAt);
        Assert.Equal(VerificationEmail.Create(message), Assert.Single(inbox.HttpRequests));
        Assert.Equal(1, (await store.FindChallengeAsync(challenge.ChallengeId, default))!.Attempts);
        // A late Kafka event and another HTTP request converge on the same mail record.
        inbox.BrokerAvailable = true;
        await inbox.PublishAsync(VerificationEmail.Create(message), default);
        Assert.Single(inbox.Messages);
        inbox.Status = DeliveryStatus.Failed;
        Assert.Equal("failed", (await browser.GetFromJsonAsync<VerificationDeliveryStatus>($"/api/auth/email/challenges/{challenge.ChallengeId}/delivery"))!.Status);
        inbox.Status = DeliveryStatus.Sent;
        Assert.Equal("sent", (await browser.GetFromJsonAsync<VerificationDeliveryStatus>($"/api/auth/email/challenges/{challenge.ChallengeId}/delivery"))!.Status);
        using var verified = await AuthTestClient.Post(browser, "/api/auth/email/verify", new { challengeId = challenge.ChallengeId, code = message.Code });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.Null((await store.FindChallengeAsync(challenge.ChallengeId, default))!.ProtectedMessage);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"/api/auth/email/challenges/{challenge.ChallengeId}/delivery")).StatusCode);
    }

    [Fact]
    public async Task Failed_http_fallback_preserves_the_original_code_and_cooldown()
    {
        var inbox = new MailInbox { BrokerAvailable = true, HttpFails = true };
        await using var app = App(inbox);
        using var browser = app.Browser();
        var challenge = await Request(browser);
        await app.Services.GetRequiredService<VerificationOutbox>().DispatchAsync(default);
        var store = app.Services.GetRequiredService<IAuthenticationStore>();
        var original = (await store.FindChallengeAsync(challenge.ChallengeId, default))!;
        var message = app.Services.GetRequiredService<VerificationPayload>().Read(original);
        app.Clock.Advance(TimeSpan.FromSeconds(61));
        using var failed = await AuthTestClient.Post(browser, $"/api/auth/email/challenges/{challenge.ChallengeId}/resend");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        using var repeated = await AuthTestClient.Post(browser, $"/api/auth/email/challenges/{challenge.ChallengeId}/resend");
        Assert.Equal(HttpStatusCode.TooManyRequests, repeated.StatusCode);
        var after = (await store.FindChallengeAsync(challenge.ChallengeId, default))!;
        Assert.False(after.Consumed);
        Assert.Equal(original.ExpiresAt, after.ExpiresAt);
        Assert.Equal(original.Attempts, after.Attempts);
        Assert.Single(inbox.HttpRequests);
        using var verified = await AuthTestClient.Post(browser, "/api/auth/email/verify", new { challengeId = challenge.ChallengeId, code = message.Code });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
    }

    [Fact]
    public async Task Outbox_and_original_payload_survive_api_restart_using_postgres_and_shared_encryption_keys()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        var keys = Path.Combine(Path.GetTempPath(), "bikehub-outbox-" + Guid.NewGuid());
        try
        {
            var inbox = new MailInbox();
            EmailCodeResponse challenge;
            SendEmailRequest expected;
            await using (var first = App(inbox, database, keys))
            {
                using var browser = first.Browser();
                challenge = await Request(browser);
                var row = (await first.Services.GetRequiredService<IAuthenticationStore>().FindChallengeAsync(challenge.ChallengeId, default))!;
                expected = VerificationEmail.Create(first.Services.GetRequiredService<VerificationPayload>().Read(row));
                Assert.Empty(inbox.Messages);
            }
            inbox.BrokerAvailable = true;
            await using var restarted = App(inbox, database, keys);
            using var newBrowser = restarted.Browser();
            await restarted.Services.GetRequiredService<VerificationOutbox>().DispatchAsync(default);
            Assert.Equal(expected, Assert.Single(inbox.Messages).Value);
            Assert.True((await restarted.Services.GetRequiredService<IAuthenticationStore>().FindChallengeAsync(challenge.ChallengeId, default))!.EventPublished);
        }
        finally
        {
            if (Directory.Exists(keys))
            {
                Directory.Delete(keys, true);
            }
        }
    }

    [Fact]
    public async Task Concurrent_fallback_is_reserved_once_in_postgres_and_does_not_extend_expired_codes()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        var inbox = new MailInbox();
        await using var app = App(inbox, database);
        using var browser = app.Browser();
        app.Clock.Advance(TimeSpan.FromTicks(1 - app.Clock.GetUtcNow().UtcTicks % TimeSpan.TicksPerMicrosecond));
        var challenge = await Request(browser);
        Assert.Equal(0, challenge.ExpiresAt.UtcTicks % TimeSpan.TicksPerMicrosecond);
        app.Clock.Advance(TimeSpan.FromSeconds(61));
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            AuthTestClient.Post(browser, $"/api/auth/email/challenges/{challenge.ChallengeId}/resend")));
        try
        {
            var accepted = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Accepted);
            Assert.Equal(challenge.ExpiresAt, (await accepted.Content.ReadFromJsonAsync<EmailCodeResponse>())!.ExpiresAt);
            Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
            Assert.Single(inbox.HttpRequests);
        }
        finally
        {
            foreach (var result in responses)
            {
                result.Dispose();
            }
        }
        app.Clock.Advance(TimeSpan.FromMinutes(5));
        await app.Services.GetRequiredService<IAuthenticationStore>().CleanupAsync(app.Clock.GetUtcNow(), default);
        Assert.Equal("expired", (await browser.GetFromJsonAsync<VerificationDeliveryStatus>($"/api/auth/email/challenges/{challenge.ChallengeId}/delivery"))!.Status);
        using var expired = await AuthTestClient.Post(browser, $"/api/auth/email/challenges/{challenge.ChallengeId}/resend");
        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
        Assert.Single(inbox.HttpRequests);
    }
}
