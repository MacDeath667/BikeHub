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
    private const string SenderCredentialId = "bikehub-mail-sender";
    private const string CurrentMailboxId = "me";
    private const string MessageIdField = "id";
    private const string InvalidGrantError = "invalid_grant";
    private const string InvalidClientError = "invalid_client";
    private const string AccessDeniedError = "access_denied";
    private const string InvalidScopeError = "invalid_scope";
    private const string OAuthError = "oauth_error";
    private const string AccessNotConfiguredReason = "accessNotConfigured";
    private const string ServiceDisabledReason = "serviceDisabled";
    private const string InsufficientPermissionsReason = "insufficientPermissions";
    private const string DomainPolicyReason = "domainPolicy";
    private const string MailServiceNotEnabledReason = "mailServiceNotEnabled";
    private const string DailyLimitExceededReason = "dailyLimitExceeded";
    private const string UserRateLimitExceededReason = "userRateLimitExceeded";
    private const string RateLimitExceededReason = "rateLimitExceeded";
    private const string ApiDisabledFailure = "gmail_api_disabled";
    private const string ScopeInsufficientFailure = "gmail_scope_insufficient";
    private const string DomainPolicyFailure = "gmail_domain_policy";
    private const string MailboxUnavailableFailure = "gmail_mailbox_unavailable";
    private const string DailyLimitFailure = "gmail_daily_limit";
    private const string PermissionDeniedFailure = "gmail_permission_denied";
    private const string ApiFailure = "gmail_api_error";
    private const int OperationTimeoutSeconds = 20;

    private readonly GmailApiOptions options;
    private readonly GoogleAuthorizationCodeFlow flow;
    private readonly UserCredential credential;
    private readonly GmailService gmail;

    public GmailApiVerificationEmailSender(
        IOptions<GmailApiOptions> gmailApiOptions,
        TimeProvider clock,
        Google.Apis.Http.IHttpClientFactory httpClientFactory)
    {
        options = gmailApiOptions.Value;
        flow = new(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = options.ClientId, ClientSecret = options.ClientSecret },
            Scopes = [GmailService.Scope.GmailSend],
            DataStore = null,
            HttpClientFactory = httpClientFactory,
            Clock = new GoogleClock(clock),
            DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None
        });
        credential = new(flow, SenderCredentialId, new TokenResponse { RefreshToken = options.RefreshToken });
        gmail = new(new BaseClientService.Initializer
        {
            ApplicationName = Constants.ApplicationName,
            HttpClientInitializer = credential,
            HttpClientFactory = httpClientFactory,
            DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None
        });
    }

    public async Task SendAsync(VerificationMessage message, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(OperationTimeoutSeconds));
        var stage = Constants.EmailTransport.OAuthStage;
        try
        {
            // The Google SDK caches the access token and refreshes it using the in-memory credential.
            await credential.GetAccessTokenForRequestAsync(cancellationToken: timeout.Token);
            stage = Constants.EmailTransport.SendStage;
            using var mail = VerificationEmail.Create(options.FromAddress, options.FromName, message);
            await using var bytes = new MemoryStream();
            await mail.WriteToAsync(bytes, timeout.Token);
            var raw = Convert.ToBase64String(bytes.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var request = gmail.Users.Messages.Send(new GmailMessage { Raw = raw }, CurrentMailboxId);
            request.Fields = MessageIdField;
            await request.ExecuteAsync(timeout.Token);
        }
        catch (TokenResponseException error)
        {
            var reason = error.Error?.Error is InvalidGrantError or InvalidClientError or AccessDeniedError or InvalidScopeError
                ? error.Error.Error : OAuthError;
            throw new EmailTransportException(Constants.EmailTransport.OAuthStage, error.GetType().Name, reason,
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
                AccessNotConfiguredReason or ServiceDisabledReason => ApiDisabledFailure,
                InsufficientPermissionsReason => ScopeInsufficientFailure,
                DomainPolicyReason => DomainPolicyFailure,
                MailServiceNotEnabledReason => MailboxUnavailableFailure,
                DailyLimitExceededReason => DailyLimitFailure,
                UserRateLimitExceededReason or RateLimitExceededReason => Constants.EmailTransport.GmailRateLimited,
                _ => null
            };
            if (reason is not null)
            {
                return reason;
            }
        }
        return error.HttpStatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => Constants.EmailTransport.AuthenticationRejected,
            System.Net.HttpStatusCode.Forbidden => PermissionDeniedFailure,
            System.Net.HttpStatusCode.TooManyRequests => Constants.EmailTransport.GmailRateLimited,
            _ => ApiFailure
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
