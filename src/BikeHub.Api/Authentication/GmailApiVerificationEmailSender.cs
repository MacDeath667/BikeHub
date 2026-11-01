using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Http;
using Google.Apis.Services;
using Microsoft.Extensions.Options;
using GmailMessage = Google.Apis.Gmail.v1.Data.Message;

namespace BikeHub.Api.Authentication;

public sealed class GmailApiVerificationEmailSender : IVerificationEmailSender, IDisposable
{
    private readonly GmailApiOptions options;
    private readonly GoogleAuthorizationCodeFlow flow;
    private readonly UserCredential credential;
    private readonly GmailService gmail;

    public GmailApiVerificationEmailSender(IOptions<AuthOptions> configuration, TimeProvider clock,
        Google.Apis.Http.IHttpClientFactory httpClientFactory)
    {
        options = configuration.Value.GmailApi;
        flow = new(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = options.ClientId, ClientSecret = options.ClientSecret },
            Scopes = [GmailService.Scope.GmailSend],
            DataStore = null,
            HttpClientFactory = httpClientFactory,
            Clock = new GoogleClock(clock),
            DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None
        });
        credential = new(flow, "bikehub-mail-sender", new TokenResponse { RefreshToken = options.RefreshToken });
        gmail = new(new BaseClientService.Initializer
        {
            ApplicationName = "BikeHub",
            HttpClientInitializer = credential,
            HttpClientFactory = httpClientFactory,
            DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None
        });
    }

    public async Task SendAsync(VerificationMessage message, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var stage = "oauth";
        try
        {
            // The Google SDK caches the access token and refreshes it using the in-memory credential.
            await credential.GetAccessTokenForRequestAsync(cancellationToken: timeout.Token);
            stage = "send";
            using var mail = VerificationEmail.Create(options.FromAddress, options.FromName, message);
            await using var bytes = new MemoryStream();
            await mail.WriteToAsync(bytes, timeout.Token);
            var raw = Convert.ToBase64String(bytes.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var request = gmail.Users.Messages.Send(new GmailMessage { Raw = raw }, "me");
            request.Fields = "id";
            await request.ExecuteAsync(timeout.Token);
        }
        catch (TokenResponseException error)
        {
            var reason = error.Error?.Error is "invalid_grant" or "invalid_client" or "access_denied" or "invalid_scope"
                ? error.Error.Error : "oauth_error";
            throw new EmailTransportException("oauth", error.GetType().Name, reason,
                error.StatusCode.HasValue ? (int)error.StatusCode.Value : null, null);
        }
        catch (GoogleApiException error)
        {
            throw new EmailTransportException(stage, error.GetType().Name,
                FailureReason(error), (int)error.HttpStatusCode, null);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            throw EmailTransportException.From(stage, error);
        }
    }

    private static string FailureReason(GoogleApiException error)
    {
        // Google messages can contain addresses or submitted content. Only emit fixed reason labels.
        foreach (var detail in error.Error?.Errors ?? [])
        {
            var reason = detail.Reason switch
            {
                "accessNotConfigured" or "serviceDisabled" => "gmail_api_disabled",
                "insufficientPermissions" => "gmail_scope_insufficient",
                "domainPolicy" => "gmail_domain_policy",
                "mailServiceNotEnabled" => "gmail_mailbox_unavailable",
                "dailyLimitExceeded" => "gmail_daily_limit",
                "userRateLimitExceeded" or "rateLimitExceeded" => "gmail_rate_limited",
                _ => null
            };
            if (reason is not null) return reason;
        }
        return error.HttpStatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "auth_rejected",
            System.Net.HttpStatusCode.Forbidden => "gmail_permission_denied",
            System.Net.HttpStatusCode.TooManyRequests => "gmail_rate_limited",
            _ => "gmail_api_error"
        };
    }

    public void Dispose()
    {
        gmail.Dispose();
        flow.Dispose();
    }

    private sealed class GoogleClock(TimeProvider clock) : Google.Apis.Util.IClock
    {
        public DateTime UtcNow => clock.GetUtcNow().UtcDateTime;
        public DateTime Now => UtcNow;
    }
}
