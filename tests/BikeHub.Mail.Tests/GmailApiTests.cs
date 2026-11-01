using System.Net;
using System.Text;
using System.Text.Json;
using BikeHub.Mail;
using BikeHub.Mail.Contracts;
using BikeHub.Mail.Transports;
using Google.Apis.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace BikeHub.Mail.Tests;

public sealed class GmailApiTests
{
    private static GmailApiOptions Settings() => new()
    {
        ClientId = "test.apps.googleusercontent.com",
        ClientSecret = "private-client-secret",
        RefreshToken = "private-refresh-token",
        FromAddress = "sender@example.test"
    };

    private static SendEmailRequest Message() => new(Guid.NewGuid(), "recipient@example.test", "Код для входа в BikeHub",
        "Код: 123456. Действует 5 минут.", DateTimeOffset.UtcNow.AddMinutes(5));

    [Fact]
    public async Task Gmail_refreshes_once_caches_token_and_sends_base64url_mime()
    {
        var handler = new FakeGoogleHandler();
        var clock = new TestClock();
        using var sender =
            new GmailApiEmailTransport(Options.Create(Settings()), clock, new FakeFactory(handler));
        await sender.SendAsync(Message(), default);
        await sender.SendAsync(Message(), default);
        Assert.Equal(1, handler.TokenRequests);
        Assert.Equal(2, handler.Messages.Count);
        var sent = handler.Messages[0];
        Assert.Equal("Bearer private-access-token", sent.Authorization);
        using var payload = JsonDocument.Parse(sent.Body);
        var raw = payload.RootElement.GetProperty("raw").GetString()!;
        Assert.DoesNotContain('+', raw);
        Assert.DoesNotContain('/', raw);
        var base64 = raw.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
        using var message = MimeMessage.Load(new MemoryStream(Convert.FromBase64String(base64)));
        Assert.Equal("sender@example.test", message.From.Mailboxes.Single().Address);
        Assert.Equal("recipient@example.test", message.To.Mailboxes.Single().Address);
        Assert.DoesNotContain("123456", message.Subject);
        Assert.Contains("123456", message.TextBody);
        Assert.Contains("5 минут", message.TextBody);
        Assert.DoesNotContain("private-refresh-token", raw);
        clock.Advance(TimeSpan.FromHours(2));
        await sender.SendAsync(Message(), default);
        Assert.Equal(2, handler.TokenRequests);
    }

    [Fact]
    public async Task Revoked_refresh_token_produces_sanitized_failure_and_does_not_send()
    {
        var handler = new FakeGoogleHandler { RejectRefresh = true };
        using var sender =
            new GmailApiEmailTransport(Options.Create(Settings()), new TestClock(), new FakeFactory(handler));
        var failure = await Assert.ThrowsAsync<EmailTransportException>(() => sender.SendAsync(Message(), default));
        Assert.Equal("oauth", failure.Stage);
        Assert.Equal("invalid_grant", failure.Reason);
        Assert.Equal(400, failure.SmtpStatus);
        Assert.Null(failure.InnerException);
        Assert.Empty(handler.Messages);
        Assert.DoesNotContain("private-refresh-token", failure.ToString());
        Assert.DoesNotContain("sender@example.test", failure.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "gmail_permission_denied")]
    [InlineData(HttpStatusCode.TooManyRequests, "gmail_rate_limited")]
    [InlineData(HttpStatusCode.BadRequest, "gmail_api_error")]
    public async Task Gmail_api_errors_do_not_expose_response_body(HttpStatusCode status, string reason)
    {
        var handler = new FakeGoogleHandler { SendStatus = status };
        using var sender =
            new GmailApiEmailTransport(Options.Create(Settings()), new TestClock(), new FakeFactory(handler));
        var failure = await Assert.ThrowsAsync<EmailTransportException>(() => sender.SendAsync(Message(), default));
        Assert.Equal("send", failure.Stage);
        Assert.Equal(reason, failure.Reason);
        Assert.DoesNotContain("recipient@example.test", failure.ToString());
        Assert.DoesNotContain("123456", failure.ToString());
        Assert.Equal(1, handler.TokenRequests);
        Assert.Single(handler.Messages); // No retry of a potentially accepted message.
    }

    [Theory]
    [InlineData("accessNotConfigured", "gmail_api_disabled")]
    [InlineData("serviceDisabled", "gmail_api_disabled")]
    [InlineData("insufficientPermissions", "gmail_scope_insufficient")]
    [InlineData("domainPolicy", "gmail_domain_policy")]
    [InlineData("mailServiceNotEnabled", "gmail_mailbox_unavailable")]
    [InlineData("dailyLimitExceeded", "gmail_daily_limit")]
    [InlineData("userRateLimitExceeded", "gmail_rate_limited")]
    [InlineData("rateLimitExceeded", "gmail_rate_limited")]
    [InlineData("private-refresh-token", "gmail_permission_denied")]
    public async Task Gmail_error_reasons_distinguish_configuration_and_permissions_without_disclosing_details(
        string googleReason, string expectedReason)
    {
        var handler = new FakeGoogleHandler { SendStatus = HttpStatusCode.Forbidden, SendReason = googleReason };
        using var sender =
            new GmailApiEmailTransport(Options.Create(Settings()), new TestClock(), new FakeFactory(handler));
        var failure = await Assert.ThrowsAsync<EmailTransportException>(() => sender.SendAsync(Message(), default));
        Assert.Equal(expectedReason, failure.Reason);
        Assert.DoesNotContain("private-", failure.ToString());
        Assert.DoesNotContain("recipient@example.test", failure.ToString());
        Assert.DoesNotContain("123456", failure.ToString());
        Assert.Single(handler.Messages);
    }

    private sealed class FakeFactory(FakeGoogleHandler handler) : Google.Apis.Http.HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => handler;
    }

    private sealed record SentMessage(string? Authorization, string Body);

    private sealed class FakeGoogleHandler : HttpMessageHandler
    {
        public int TokenRequests { get; private set; }
        public bool RejectRefresh { get; init; }
        public HttpStatusCode SendStatus { get; init; } = HttpStatusCode.OK;
        public string? SendReason { get; init; }
        public List<SentMessage> Messages { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.DoesNotContain("private-", request.RequestUri!.AbsoluteUri);
            if (request.RequestUri.Host == "oauth2.googleapis.com")
            {
                TokenRequests++;
                var form = await request.Content!.ReadAsStringAsync(cancellationToken);
                Assert.Contains("refresh_token=private-refresh-token", form);
                return RejectRefresh
                    ? Json(HttpStatusCode.BadRequest,
                        new
                        {
                            error = "invalid_grant", error_description = "sender@example.test private-refresh-token"
                        })
                    : Json(HttpStatusCode.OK,
                        new { access_token = "private-access-token", token_type = "Bearer", expires_in = 3600 });
            }

            Assert.Equal("gmail.googleapis.com", request.RequestUri.Host);
            Assert.Equal("/gmail/v1/users/me/messages/send", request.RequestUri.AbsolutePath);
            string body;
            if (request.Content!.Headers.ContentEncoding.Contains("gzip"))
            {
                await using var encoded = await request.Content.ReadAsStreamAsync(cancellationToken);
                await using var decoded =
                    new System.IO.Compression.GZipStream(encoded, System.IO.Compression.CompressionMode.Decompress);
                using var reader = new StreamReader(decoded, Encoding.UTF8);
                body = await reader.ReadToEndAsync(cancellationToken);
            }
            else
            {
                body = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            Messages.Add(new(request.Headers.Authorization?.ToString(), body));
            return SendStatus == HttpStatusCode.OK
                ? Json(SendStatus, new { id = "test-message-id" })
                : Json(SendStatus, new
                {
                    error = new
                    {
                        code = (int)SendStatus,
                        message = "recipient@example.test 123456",
                        errors = new[]
                        {
                            new { reason = SendReason, message = "recipient@example.test 123456", domain = "global" }
                        }
                    }
                });
        }

        private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
            { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

        protected override void Dispose(bool disposing)
        {
        } // Shared by the SDK's token and mail clients in this test only.
    }
}
