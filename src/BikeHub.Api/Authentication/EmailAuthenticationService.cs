using Microsoft.Extensions.Options;

namespace BikeHub.Api.Authentication;

public sealed class EmailAuthenticationService(IAuthenticationStore store, IVerificationEmailSender sender,
    OtpSecurity security, TimeProvider clock, IOptions<AuthOptions> configuration,
    ILogger<EmailAuthenticationService> logger)
{
    private const string DefaultDisplayName = "Участник";
    private const string ThrottledEvent = "throttled";
    private const string DeliveryFailedEvent = "delivery_failed";
    private const string CodeRequestedEvent = "code_requested";
    private const string CodeRejectedEvent = "code_rejected";
    private const string EmailVerifiedEvent = "email_verified";
    private const string TransportFailureLog = "Verification email delivery failed ({FailureType}; stage={Stage}; reason={Reason}; smtpStatus={SmtpStatus}; enhancedStatus={EnhancedStatus})";
    private const string DeliveryFailureLog = "Verification email delivery failed ({FailureType})";

    public async Task<EmailCodeResponse?> RequestAsync(string email, string? name, string binding,
        CancellationToken cancellationToken)
    {
        var options = configuration.Value;
        var now = clock.GetUtcNow();
        var id = Guid.NewGuid();
        var code = OtpSecurity.NewCode();
        var challenge = new EmailChallenge(id, email, string.IsNullOrWhiteSpace(name) ? DefaultDisplayName : name.Trim(),
            security.HashCode(id, code), security.Hash(Constants.BrowserBindingPurpose, binding), now, now.AddMinutes(options.CodeMinutes));
        if (!await store.TryCreateChallengeAsync(challenge, options, cancellationToken))
        {
            AuthTelemetry.Record(ThrottledEvent);
            return null;
        }
        try
        {
            await sender.SendAsync(new(id, email, code, challenge.BindingHash, challenge.ExpiresAt, options.CodeMinutes), cancellationToken);
            await store.SetDeliveredAsync(id, true, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await store.SetDeliveredAsync(id, false, cancellationToken);
            // SMTP exceptions may contain addresses or message contents. Do not export them to logs.
            if (error is EmailTransportException transport)
            {
                logger.LogWarning(TransportFailureLog,
                    transport.FailureType, transport.Stage, transport.Reason, transport.SmtpStatus, transport.EnhancedStatus);
            }
            else
            {
                logger.LogWarning(DeliveryFailureLog, error.GetType().Name);
            }

            AuthTelemetry.Record(DeliveryFailedEvent);
            throw new EmailDeliveryException();
        }
        AuthTelemetry.Record(CodeRequestedEvent);
        return new(id, challenge.ExpiresAt, now.AddSeconds(options.ResendSeconds));
    }

    public async Task<ConfirmationResult> VerifyAsync(Guid id, string code, string binding, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var result = await store.ConfirmAsync(id, security.HashCode(id, code), security.Hash(Constants.BrowserBindingPurpose, binding),
            now, now.AddDays(configuration.Value.SessionDays), configuration.Value.MaxAttempts, cancellationToken);
        AuthTelemetry.Record(result.Session is null ? CodeRejectedEvent : EmailVerifiedEvent);
        return result;
    }
}

public sealed class EmailDeliveryException : Exception { }
