using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BikeHub.Api.Authentication;
using BikeHub.Mail.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace BikeHub.Api.Tests;

public sealed class MailServiceTests
{
    [Fact]
    public async Task Api_uses_remote_mail_service_instead_of_mail_credentials()
    {
        await using var app = new AuthFactory();
        app.Settings["Authentication:EmailProvider"] = "MailService";
        app.Settings["MailService:BaseUrl"] = "http://mail.example.test/";
        app.Settings["MailService:ApiKey"] = new string('M', 32);
        using var browser = app.Browser();
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/auth/session")).StatusCode);
        Assert.IsType<MailServiceEmailSender>(app.Services.GetRequiredService<IVerificationEmailSender>());
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("file:///tmp/mail", "12345678901234567890123456789012")]
    [InlineData("http://mail/", "short")]
    public void Invalid_mail_service_configuration_fails_startup(string url, string key)
    {
        using var app = new AuthFactory();
        app.Settings["Authentication:EmailProvider"] = "MailService";
        app.Settings["MailService:BaseUrl"] = url;
        app.Settings["MailService:ApiKey"] = key;
        Assert.Contains("Configure a valid MailService", Assert.ThrowsAny<Exception>(() => app.Browser()).ToString());
    }

    [Fact]
    public async Task Retry_reuses_message_id_and_does_not_send_browser_binding_to_mail_service()
    {
        var handler = new MailHandler();
        using var client = new HttpClient(handler);
        MailServiceEmailSender.Configure(client, new() { BaseUrl = "http://mail/", ApiKey = new string('M', 32) });
        var sender = new MailServiceEmailSender(client);
        var message = new VerificationMessage(Guid.NewGuid(), "recipient@example.test", "123456", "private-binding", DateTimeOffset.UtcNow.AddMinutes(5), 5);
        await sender.SendAsync(message, default);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(message.ChallengeId.ToString(), request.Key);
            Assert.DoesNotContain("private-binding", request.Body);
            Assert.Contains("123456", request.Body);
        });
        Assert.Equal(handler.Requests[0], handler.Requests[1]);
    }

    private sealed class MailHandler : HttpMessageHandler
    {
        public List<(string Key, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Headers.GetValues("Idempotency-Key").Single(), body));
            if (Requests.Count == 1)
            {
                throw new HttpRequestException("uncertain network outcome");
            }
            var message = JsonSerializer.Deserialize<SendEmailRequest>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            return new(HttpStatusCode.Accepted)
            {
                Content = JsonContent.Create(new { messageId = message.MessageId, status = "queued", attempts = 0,
                    createdAt = DateTimeOffset.UtcNow, updatedAt = DateTimeOffset.UtcNow })
            };
        }
    }
}
