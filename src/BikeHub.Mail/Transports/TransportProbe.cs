using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace BikeHub.Mail.Transports;

public sealed record TransportProbeResult(bool Ok, string Stage, string Reason, int? Status = null, string? EnhancedStatus = null);

public static class TransportProbe
{
    private const string ConnectStage = "connect";
    private const string Accepted = "accepted";

    public static async Task<TransportProbeResult> CheckAsync(IConfiguration configuration, CancellationToken cancellationToken, bool development = false)
    {
        var provider = configuration["Mail:Provider"];
        var stage = ConnectStage;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Constants.SendTimeoutSeconds));
            if (provider == Constants.SmtpProvider)
            {
                var smtp = configuration.GetSection("Smtp").Get<SmtpOptions>() ?? new();
                var socketOptions = SmtpEmailTransport.SocketOptions(smtp, development);
                if (socketOptions != SecureSocketOptions.None && (string.IsNullOrWhiteSpace(smtp.Username) || string.IsNullOrWhiteSpace(smtp.Password)))
                {
                    return new(false, stage, "missing_configuration");
                }
                using var client = new SmtpClient { Timeout = 10_000 };
                await client.ConnectAsync(smtp.Host, smtp.Port, socketOptions, timeout.Token);
                if (socketOptions != SecureSocketOptions.None)
                {
                    stage = Constants.EmailTransport.AuthenticateStage;
                    await client.AuthenticateAsync(SmtpEmailTransport.PasswordMechanism(client.AuthenticationMechanisms,
                        smtp.Username, smtp.Password), timeout.Token);
                }
                return new(true, stage, Accepted);
            }
            if (provider == Constants.GmailProvider)
            {
                stage = Constants.EmailTransport.OAuthStage;
                var gmail = configuration.GetSection("GmailApi").Get<GmailApiOptions>() ?? new();
                using var flow = new GoogleAuthorizationCodeFlow(new()
                {
                    ClientSecrets = new ClientSecrets { ClientId = gmail.ClientId, ClientSecret = gmail.ClientSecret },
                    Scopes = [GmailService.Scope.GmailSend], DataStore = null
                });
                var token = await flow.RefreshTokenAsync(Constants.ApplicationName, gmail.RefreshToken, timeout.Token);
                return string.IsNullOrEmpty(token.AccessToken) || !(token.Scope ?? string.Empty).Split(' ').Contains(GmailService.Scope.GmailSend)
                    ? new(false, stage, "gmail_scope_missing") : new(true, stage, Accepted);
            }
            return new(false, stage, "unsupported_provider");
        }
        catch (TokenResponseException error)
        {
            var reason = error.Error?.Error is "invalid_grant" or "invalid_client" or "invalid_scope" or "access_denied"
                ? error.Error.Error : "oauth_rejected";
            return new(false, stage, reason, error.StatusCode.HasValue ? (int)error.StatusCode : null);
        }
        catch (Exception error)
        {
            var safe = EmailTransportException.From(stage, error);
            return new(false, safe.Stage, safe.Reason, safe.SmtpStatus, safe.EnhancedStatus);
        }
    }
}
