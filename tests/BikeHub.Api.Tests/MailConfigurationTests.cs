using System.Net;
using BikeHub.Api.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BikeHub.Api.Tests;

public sealed class MailConfigurationTests
{
    [Fact]
    public async Task Smtp_sender_uses_the_root_configuration_section()
    {
        await using var app = new AuthFactory();
        app.Settings["Authentication:EmailProvider"] = "Smtp";
        app.Settings["Smtp:Host"] = "smtp.example.test";
        app.Settings["Smtp:Port"] = "465";
        app.Settings["Smtp:Security"] = "SslOnConnect";
        app.Settings["Smtp:Username"] = "sender@example.test";
        app.Settings["Smtp:Password"] = "test-only-password";
        app.Settings["Smtp:FromAddress"] = "sender@example.test";
        app.Settings["Smtp:FromName"] = "Configured sender";
        using var browser = app.Browser();
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/auth/session")).StatusCode);
        Assert.IsType<SmtpVerificationEmailSender>(app.Services.GetRequiredService<IVerificationEmailSender>());
        var options = app.Services.GetRequiredService<IOptions<SmtpOptions>>().Value;
        Assert.Equal("smtp.example.test", options.Host);
        Assert.Equal(465, options.Port);
        Assert.Equal("SslOnConnect", options.Security);
        Assert.Equal("sender@example.test", options.Username);
        Assert.Equal("test-only-password", options.Password);
        Assert.Equal("sender@example.test", options.FromAddress);
        Assert.Equal("Configured sender", options.FromName);
    }

    [Theory]
    [InlineData("Smtp", "Configure a valid SMTP")]
    [InlineData("GmailApi", "Configure a Google OAuth client")]
    public void Nested_authentication_settings_cannot_hide_missing_root_mail_configuration(string provider, string message)
    {
        using var app = new AuthFactory();
        app.Settings["Authentication:EmailProvider"] = provider;
        var prefix = "Authentication:" + provider + ":";
        app.Settings[prefix + "Username"] = "sender@example.test";
        app.Settings[prefix + "Password"] = "test-only-password";
        app.Settings[prefix + "FromAddress"] = "sender@example.test";
        app.Settings[prefix + "ClientId"] = "test.apps.googleusercontent.com";
        app.Settings[prefix + "ClientSecret"] = "private-client-secret";
        app.Settings[prefix + "RefreshToken"] = "private-refresh-token";
        var failure = Assert.ThrowsAny<Exception>(() => app.Browser());
        Assert.Contains(message, failure.ToString());
    }
}
