using System.Security.Cryptography;
using System.Text.Json;
using Google.Apis.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Http;

namespace BikeHub.Tools;

public static class GmailAuthorization
{
    private const string GmailScope = "https://www.googleapis.com/auth/gmail.send";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string ClientIdSuffix = ".apps.googleusercontent.com";
    private const string CallbackMessage = "Авторизация получена. Вернитесь в терминал BikeHub.";
    private static readonly string[] Scopes = ["openid", "https://www.googleapis.com/auth/userinfo.email", GmailScope];

    public static ClientSecrets LoadClient(string filename, string repositoryRoot)
    {
        var path = Program.Expand(filename);
        if (Path.GetRelativePath(repositoryRoot, path) is var relative && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !File.Exists(path) || new FileInfo(path).Length > 65_536)
        {
            throw new ArgumentException();
        }
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var installed = document.RootElement.GetProperty("installed");
        var authorization = installed.GetProperty("auth_uri").GetString();
        var clientId = installed.GetProperty("client_id").GetString();
        var secret = installed.GetProperty("client_secret").GetString();
        if (authorization is not ("https://accounts.google.com/o/oauth2/auth" or "https://accounts.google.com/o/oauth2/v2/auth") ||
            installed.GetProperty("token_uri").GetString() != TokenEndpoint ||
            clientId?.EndsWith(ClientIdSuffix, StringComparison.Ordinal) != true || string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException();
        }
        return new() { ClientId = clientId, ClientSecret = secret };
    }

    public static async Task<Dictionary<string, string?>> AuthorizeAsync(string filename, string sender, string repositoryRoot)
    {
        var client = LoadClient(filename, repositoryRoot);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var flow = new PkceGoogleAuthorizationCodeFlow(new()
        {
            ClientSecrets = client, Scopes = Scopes, DataStore = null, Nonce = nonce,
            DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None
        });
        var receiver = new DisplayCodeReceiver();
        var request = (GoogleAuthorizationCodeRequestUrl)flow.CreateAuthorizationCodeRequest(receiver.RedirectUri, out var verifier);
        request.State = state;
        request.Nonce = nonce;
        request.Prompt = "consent";
        request.AccessType = "offline";
        request.LoginHint = sender;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var response = await receiver.ReceiveCodeAsync(request, timeout.Token);
        if (response.State != state || !string.IsNullOrEmpty(response.Error) || string.IsNullOrEmpty(response.Code))
        {
            throw new InvalidOperationException();
        }
        var token = await flow.ExchangeCodeForTokenAsync("BikeHub.Tools", response.Code, verifier, receiver.RedirectUri, timeout.Token);
        var identity = await GoogleJsonWebSignature.ValidateAsync(token.IdToken,
            new GoogleJsonWebSignature.ValidationSettings { Audience = [client.ClientId] });
        ValidateIdentity(identity.Email, identity.EmailVerified, identity.Nonce, sender, nonce, token.Scope, token.RefreshToken);
        return new()
        {
            ["Mail:Provider"] = "GmailApi", ["GmailApi:ClientId"] = client.ClientId,
            ["GmailApi:ClientSecret"] = client.ClientSecret, ["GmailApi:RefreshToken"] = token.RefreshToken,
            ["GmailApi:FromAddress"] = identity.Email.ToLowerInvariant()
        };
    }

    public static void ValidateIdentity(string email, bool verified, string nonce, string expectedSender,
        string expectedNonce, string? scopes, string? refreshToken)
    {
        if (!verified || nonce != expectedNonce || CanonicalEmail(email) != CanonicalEmail(expectedSender) ||
            !(scopes ?? string.Empty).Split(' ').Contains(GmailScope) || string.IsNullOrEmpty(refreshToken))
        {
            throw new InvalidOperationException();
        }
    }

    private static string CanonicalEmail(string value)
    {
        var parts = value.Trim().ToLowerInvariant().Split('@');
        if (parts.Length != 2)
        {
            throw new ArgumentException();
        }
        return parts[1] is "gmail.com" or "googlemail.com" ? parts[0].Replace(".", string.Empty) + "@gmail.com" : string.Join('@', parts);
    }

    private sealed class DisplayCodeReceiver() : LocalServerCodeReceiver(CallbackMessage, CallbackUriChooserStrategy.ForceLoopbackIp)
    {
        protected override bool OpenBrowser(string url)
        {
            Console.WriteLine("Откройте ссылку на этом компьютере и выберите аккаунт отправителя. Ожидание: 5 минут.");
            Console.WriteLine(url);
            return true;
        }
    }
}
