using System.Net;
using BikeHub.Api.Authentication;
using MailKit.Security;

namespace BikeHub.Api.Tests;

public sealed class SmtpAuthenticationTests
{
    [Fact]
    public void Password_authentication_selects_one_non_oauth_mechanism()
    {
        var supported = new HashSet<string> { "PLAIN", "LOGIN", "XOAUTH2", "OAUTHBEARER" };
        Assert.IsType<SaslMechanismPlain>(SmtpVerificationEmailSender.PasswordMechanism(supported, "sender@example.test", "fake-password"));
        supported.Remove("PLAIN");
        Assert.IsType<SaslMechanismLogin>(SmtpVerificationEmailSender.PasswordMechanism(supported, "sender@example.test", "fake-password"));
        supported.Remove("LOGIN");
        Assert.Throws<NotSupportedException>(() => SmtpVerificationEmailSender.PasswordMechanism(supported, "sender@example.test", "fake-password"));
    }

    [Theory]
    [InlineData("535 5.7.8 BadCredentials sender@example.test private-password")]
    [InlineData("535: 5.7.8 BadCredentials sender@example.test private-password")]
    public void Transport_failure_keeps_only_stage_and_numeric_status(string diagnostic)
    {
        var original = new AuthenticationException(diagnostic);
        var failure = EmailTransportException.From("authenticate", original);
        Assert.Equal("auth_rejected", failure.Reason);
        Assert.Equal(535, failure.SmtpStatus);
        Assert.Equal("5.7.8", failure.EnhancedStatus);
        Assert.Equal("authenticate", failure.Stage);
        Assert.Null(failure.InnerException);
        Assert.DoesNotContain("sender@example.test", failure.ToString());
        Assert.DoesNotContain("private-password", failure.ToString());
    }

    [Fact]
    public async Task Delivery_log_reports_authentication_failure_without_account_details()
    {
        await using var app = new AuthFactory { Sender = new RejectedSmtpSender() };
        using var browser = app.Browser();
        using var result = await AuthTestClient.Post(browser, "/api/auth/email/challenges", new { email = "recipient@example.test" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        var logs = string.Join('\n', app.Logs.Entries);
        Assert.Contains("stage=authenticate", logs);
        Assert.Contains("reason=auth_rejected", logs);
        Assert.Contains("smtpStatus=535", logs);
        Assert.DoesNotContain("sender@example.test", logs);
        Assert.DoesNotContain("recipient@example.test", logs);
        Assert.DoesNotContain("private-password", logs);
    }

    private sealed class RejectedSmtpSender : IVerificationEmailSender
    {
        public Task SendAsync(VerificationMessage message, CancellationToken cancellationToken) =>
            throw EmailTransportException.From("authenticate",
                new AuthenticationException("535 5.7.8 BadCredentials sender@example.test private-password"));
    }
}
