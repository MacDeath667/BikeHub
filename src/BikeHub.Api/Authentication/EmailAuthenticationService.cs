using Microsoft.Extensions.Options;
using BikeHub.Dal.Entities;
using BikeHub.Mail.Contracts;
using BikeHub.Api.Services;

namespace BikeHub.Api.Authentication;

public sealed class EmailAuthenticationService(IAuthenticationStore store, IVerificationEmailSender sender,
    VerificationPayload payload, OtpSecurity security, TimeProvider clock, IOptions<AuthOptions> configuration,
    ILogger<EmailAuthenticationService> logger)
{
    private const string InvalidChallengeTitle = "Запрос кода не найден или уже завершён.";
    private const string ExpiredChallengeTitle = "Срок действия кода истёк. Запросите новый код.";
    private const string CooldownTitle = "Повторить отправку можно через минуту.";
    private const int MinimumResendSeconds = 60;
    private const string DefaultDisplayName = "Участник";
    private const string ThrottledEvent = "throttled";
    private const string DeliveryFailedEvent = "delivery_failed";
    private const string CodeRequestedEvent = "code_requested";
    private const string CodeRejectedEvent = "code_rejected";
    private const string EmailVerifiedEvent = "email_verified";
    private const string DeliveryFailureLog = "Verification email delivery failed ({FailureType})";

    private DateTimeOffset UtcNow()
    {
        var now = clock.GetUtcNow();
        // PostgreSQL timestamps have microsecond precision. Persist and return exactly the same deadline.
        return new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    }

    public async Task<EmailCodeResponse?> RequestAsync(string email, string? name, string binding,
        CancellationToken cancellationToken, AuthenticationPurpose purpose = AuthenticationPurpose.Registration)
    {
        var options = configuration.Value;
        var now = UtcNow();
        var id = Guid.NewGuid();
        var code = OtpSecurity.NewCode();
        var challenge = new EmailChallenge(id, email, string.IsNullOrWhiteSpace(name) ? DefaultDisplayName : name.Trim(),
            security.HashCode(id, code), security.Hash(Constants.BrowserBindingPurpose, binding), now, now.AddMinutes(options.CodeMinutes), Purpose: purpose);
        var message = new VerificationMessage(id, email, code, challenge.BindingHash, challenge.ExpiresAt, options.CodeMinutes, purpose);
        challenge = challenge with { ProtectedMessage = payload.Protect(message),
            Delivered = options.EmailProvider == Constants.EmailProviders.MailService };
        if (!await store.TryCreateChallengeAsync(challenge, options, cancellationToken))
        {
            AuthTelemetry.Record(ThrottledEvent);
            return null;
        }
        try
        {
            if (options.EmailProvider != Constants.EmailProviders.MailService)
            {
                await sender.SendAsync(message, cancellationToken);
                await store.SetDeliveredAsync(id, true, cancellationToken);
                await store.MarkPublishedAsync(id, cancellationToken);
            }
            // MailService requests are durably queued in the challenge row; the outbox publishes to Kafka.
            // Delivered is a legacy eligibility flag, not proof of final email delivery.
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await store.SetDeliveredAsync(id, false, cancellationToken);
            // Never log response bodies, addresses or codes from the mail service.
            logger.LogWarning(DeliveryFailureLog, error.GetType().Name);

            AuthTelemetry.Record(DeliveryFailedEvent);
            throw new EmailDeliveryException();
        }
        AuthTelemetry.Record(CodeRequestedEvent);
        return new(id, challenge.ExpiresAt, now.AddSeconds(Math.Max(MinimumResendSeconds, options.ResendSeconds)));
    }

    private async Task<EmailChallenge> BoundChallengeAsync(Guid id, string binding, CancellationToken cancellationToken)
    {
        var challenge = await store.FindChallengeAsync(id, cancellationToken);
        if (challenge is null || challenge.Consumed || challenge.Attempts >= configuration.Value.MaxAttempts ||
            (challenge.ProtectedMessage is null && challenge.ExpiresAt > clock.GetUtcNow()) || !OtpSecurity.EqualsHash(challenge.BindingHash, security.Hash(Constants.BrowserBindingPurpose, binding)))
        {
            throw new CommunityProblem(404, InvalidChallengeTitle);
        }
        return challenge;
    }

    public async Task<EmailCodeResponse> ResendAsync(Guid id, string binding, CancellationToken cancellationToken)
    {
        var challenge = await BoundChallengeAsync(id, binding, cancellationToken);
        var now = UtcNow();
        if (challenge.ExpiresAt <= now)
        {
            throw new CommunityProblem(410, ExpiredChallengeTitle);
        }
        var cooldown = Math.Max(MinimumResendSeconds, configuration.Value.ResendSeconds);
        if (!await store.TryReserveResendAsync(id, challenge.BindingHash, now, cooldown, configuration.Value.MaxAttempts, cancellationToken))
        {
            throw new CommunityProblem(429, CooldownTitle);
        }
        try
        {
            await sender.SendAsync(payload.Read(challenge), cancellationToken);
            await store.SetDeliveredAsync(id, true, cancellationToken);
            // Stop unpublished events after HTTP acceptance; in-flight events remain safe through mail-service deduplication.
            await store.MarkPublishedAsync(id, cancellationToken);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(DeliveryFailureLog, error.GetType().Name);
            // An uncertain HTTP outcome must not consume the challenge: its code may already be in the user's inbox.
            throw new EmailDeliveryException();
        }
        return new(id, challenge.ExpiresAt, now.AddSeconds(cooldown));
    }

    public async Task<VerificationDeliveryStatus> DeliveryAsync(Guid id, string binding, CancellationToken cancellationToken)
    {
        var challenge = await BoundChallengeAsync(id, binding, cancellationToken);
        if (challenge.ExpiresAt <= clock.GetUtcNow())
        {
            return new(Constants.VerificationStatuses.Expired);
        }
        if (configuration.Value.EmailProvider == Constants.EmailProviders.Development)
        {
            return new(challenge.Delivered ? Constants.VerificationStatuses.Sent : Constants.VerificationStatuses.Pending);
        }
        try
        {
            var status = sender is IVerificationDeliveryStatus remote ? await remote.GetStatusAsync(id, cancellationToken) : null;
            return new(status switch
            {
                DeliveryStatus.Sent => Constants.VerificationStatuses.Sent,
                DeliveryStatus.Failed => Constants.VerificationStatuses.Failed,
                DeliveryStatus.Expired => Constants.VerificationStatuses.Expired,
                DeliveryStatus.Unknown => Constants.VerificationStatuses.Unknown,
                _ => Constants.VerificationStatuses.Pending
            });
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(DeliveryFailureLog, error.GetType().Name);
            return new(Constants.VerificationStatuses.Unavailable);
        }
    }

    public async Task<ConfirmationResult> VerifyAsync(Guid id, string code, string binding, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var result = await store.ConfirmAsync(id, security.HashCode(id, code), security.Hash(Constants.BrowserBindingPurpose, binding),
            now, now.AddDays(configuration.Value.SessionDays), configuration.Value.MaxAttempts, cancellationToken);
        AuthTelemetry.Record(result.Session is null ? CodeRejectedEvent : EmailVerifiedEvent);
        return result;
    }
}

public sealed class EmailDeliveryException : Exception { }
