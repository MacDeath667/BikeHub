using Microsoft.Extensions.Options;

namespace BikeHub.Api.Authentication;

public sealed class EmailAuthenticationService(IAuthenticationStore store, IVerificationEmailSender sender,
    OtpSecurity security, TimeProvider clock, IOptions<AuthOptions> configuration,
    ILogger<EmailAuthenticationService> logger)
{
    public async Task<EmailCodeResponse?> RequestAsync(string email, string? name, string binding,
        CancellationToken cancellationToken)
    {
        var options = configuration.Value;
        var now = clock.GetUtcNow();
        var id = Guid.NewGuid();
        var code = OtpSecurity.NewCode();
        var challenge = new EmailChallenge(id, email, string.IsNullOrWhiteSpace(name) ? "Участник" : name.Trim(),
            security.HashCode(id, code), security.Hash("browser", binding), now, now.AddMinutes(options.CodeMinutes));
        if (!await store.TryCreateChallengeAsync(challenge, options, cancellationToken))
        {
            AuthTelemetry.Record("throttled");
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
            logger.LogWarning("Verification email delivery failed ({FailureType})", error.GetType().Name);
            AuthTelemetry.Record("delivery_failed");
            throw new EmailDeliveryException();
        }
        AuthTelemetry.Record("code_requested");
        return new(id, challenge.ExpiresAt, now.AddSeconds(options.ResendSeconds));
    }

    public async Task<ConfirmationResult> VerifyAsync(Guid id, string code, string binding, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var result = await store.ConfirmAsync(id, security.HashCode(id, code), security.Hash("browser", binding),
            now, now.AddDays(configuration.Value.SessionDays), configuration.Value.MaxAttempts, cancellationToken);
        AuthTelemetry.Record(result.Session is null ? "code_rejected" : "email_verified");
        return result;
    }
}

public sealed class EmailDeliveryException : Exception { }
