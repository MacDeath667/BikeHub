using BikeHub.Tools;

namespace BikeHub.Mail.Tests;

public sealed class OperatorToolsTests
{
    private const string GmailScope = "https://www.googleapis.com/auth/gmail.send";

    [Fact]
    public void Google_identity_requires_expected_account_nonce_scope_and_refresh_token()
    {
        GmailAuthorization.ValidateIdentity("rider.name@gmail.com", true, "nonce", "ridername@googlemail.com", "nonce", GmailScope, "test-token");
        Assert.Throws<InvalidOperationException>(() => GmailAuthorization.ValidateIdentity("other@gmail.com", true, "nonce", "rider@gmail.com", "nonce", GmailScope, "test-token"));
        Assert.Throws<InvalidOperationException>(() => GmailAuthorization.ValidateIdentity("rider@gmail.com", false, "nonce", "rider@gmail.com", "nonce", GmailScope, "test-token"));
        Assert.Throws<InvalidOperationException>(() => GmailAuthorization.ValidateIdentity("rider@gmail.com", true, "wrong", "rider@gmail.com", "nonce", GmailScope, "test-token"));
        Assert.Throws<InvalidOperationException>(() => GmailAuthorization.ValidateIdentity("rider@gmail.com", true, "nonce", "rider@gmail.com", "nonce", "openid", "test-token"));
        Assert.Throws<InvalidOperationException>(() => GmailAuthorization.ValidateIdentity("rider@gmail.com", true, "nonce", "rider@gmail.com", "nonce", GmailScope, null));
    }

    [Fact]
    public void OAuth_client_file_rejects_repository_paths_and_non_google_endpoints()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bikehub-oauth-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var filename = Path.Combine(folder, "client.json");
            File.WriteAllText(filename, """
                {"installed":{"auth_uri":"https://accounts.google.com/o/oauth2/auth","token_uri":"https://oauth2.googleapis.com/token","client_id":"test.apps.googleusercontent.com","client_secret":"test-secret"}}
                """);
            Assert.Equal("test.apps.googleusercontent.com", GmailAuthorization.LoadClient(filename, Path.Combine(folder, "repo")).ClientId);
            Assert.Throws<ArgumentException>(() => GmailAuthorization.LoadClient(filename, folder));
            File.WriteAllText(filename, File.ReadAllText(filename).Replace("https://oauth2.googleapis.com/token", "https://untrusted.example/token"));
            Assert.Throws<ArgumentException>(() => GmailAuthorization.LoadClient(filename, Path.Combine(folder, "repo")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
