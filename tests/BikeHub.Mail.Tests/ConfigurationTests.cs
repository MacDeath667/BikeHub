using Microsoft.Extensions.Configuration;

namespace BikeHub.Mail.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void Production_rejects_dry_run_and_missing_api_key()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Mail"] = "Host=unused;Database=unused",
            ["Mail:Provider"] = "Development",
            ["Mail:ApiKey"] = new string('M', 32)
        };
        Assert.Throws<InvalidOperationException>(() => MailProgram.ValidateConfiguration(Config(settings), false));
        MailProgram.ValidateConfiguration(Config(settings), true);
        settings["Mail:ApiKey"] = string.Empty;
        Assert.Throws<InvalidOperationException>(() => MailProgram.ValidateConfiguration(Config(settings), true));
    }

    [Fact]
    public void Gmail_root_settings_are_independent_of_authentication_and_smtp()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Mail"] = "Host=unused;Database=unused",
            ["Mail:Provider"] = "GmailApi",
            ["Mail:ApiKey"] = new string('M', 32),
            ["GmailApi:ClientId"] = "test.apps.googleusercontent.com",
            ["GmailApi:ClientSecret"] = "test-secret",
            ["GmailApi:RefreshToken"] = "test-token",
            ["GmailApi:FromAddress"] = "sender@example.test"
        };
        MailProgram.ValidateConfiguration(Config(settings), false);
        settings.Remove("GmailApi:RefreshToken");
        settings["Authentication:GmailApi:RefreshToken"] = "old-test-token";
        Assert.Throws<InvalidOperationException>(() => MailProgram.ValidateConfiguration(Config(settings), false));
    }

    private static IConfiguration Config(Dictionary<string, string?> settings) => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    [Fact]
    public void Anonymous_smtp_is_development_only_and_never_accepts_credentials_without_tls()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Mail"] = "Host=unused;Database=unused",
            ["Mail:Provider"] = "Smtp",
            ["Mail:ApiKey"] = new string('M', 32),
            ["Smtp:Host"] = "mailpit",
            ["Smtp:Port"] = "1025",
            ["Smtp:Security"] = "None",
            ["Smtp:FromAddress"] = "no-reply@bikehub.local"
        };
        MailProgram.ValidateConfiguration(Config(settings), true);
        Assert.Throws<InvalidOperationException>(() => MailProgram.ValidateConfiguration(Config(settings), false));
        settings["Smtp:Username"] = "private-user";
        settings["Smtp:Password"] = "private-password";
        Assert.Throws<InvalidOperationException>(() => MailProgram.ValidateConfiguration(Config(settings), true));
        Assert.Throws<InvalidOperationException>(() => Transports.SmtpEmailTransport.SocketOptions(
            new() { Security = "None", Username = "private-user", Password = "private-password" }, true));
    }
}
