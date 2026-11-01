using System.ComponentModel.DataAnnotations;
using BikeHub.Application.Community;

namespace BikeHub.Api.Authentication;

public sealed class AuthOptions
{
    public bool Enabled { get; set; }
    public string Store { get; set; } = Constants.AuthenticationStores.Postgres;
    public string EmailProvider { get; set; } = Constants.EmailProviders.Smtp;
    public string OtpSecret { get; set; } = string.Empty;
    public string DataProtectionPath { get; set; } = string.Empty;
    public int CodeMinutes { get; set; } = 5;
    public int ResendSeconds { get; set; } = 60;
    public int MaxAttempts { get; set; } = 5;
    public int MaxPerEmailHour { get; set; } = 5;
    public int MaxPerDay { get; set; } = 100;
    public int SessionDays { get; set; } = 14;

}

public sealed class GmailApiOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = Constants.ApplicationName;
}

public sealed class SmtpOptions
{
    private const string DefaultHost = "smtp.gmail.com";

    public string Host { get; set; } = DefaultHost;
    public int Port { get; set; } = 587;
    public string Security { get; set; } = Constants.SmtpSecurity.StartTls;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = Constants.ApplicationName;
}

public sealed record RequestEmailCode(
    [Required, EmailAddress, StringLength(Constants.MaximumEmailLength)] string Email,
    [StringLength(60, MinimumLength = Constants.MinimumDisplayNameLength)] string? Name);

public sealed record VerifyEmailCode(
    Guid ChallengeId,
    [Required, RegularExpression("^[0-9]{6}$")] string Code);

public sealed record EmailCodeResponse(Guid ChallengeId, DateTimeOffset ExpiresAt, DateTimeOffset ResendAt);
public sealed record AuthBootstrap(UserDto? User, bool EmailConfirmed, string? MaskedEmail,
    bool PhoneConfirmed, string CsrfToken, bool Enabled, bool DevelopmentMailbox);
public sealed record AuthAccount(Guid Id, string Email, string DisplayName, DateTimeOffset EmailConfirmedAt);
public sealed record AuthSession(Guid Id, AuthAccount Account, DateTimeOffset ExpiresAt);
public sealed record EmailChallenge(Guid Id, string Email, string DisplayName, string CodeHash,
    string BindingHash, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    int Attempts = 0, bool Delivered = false, bool Consumed = false);
public enum ConfirmationFailure { None, Invalid, TooManyAttempts }
public sealed record ConfirmationResult(AuthSession? Session, ConfirmationFailure Failure);
public sealed record VerificationMessage(Guid ChallengeId, string Destination, string Code,
    string BindingHash, DateTimeOffset ExpiresAt, int ValidMinutes);

public interface IVerificationEmailSender
{
    Task SendAsync(VerificationMessage message, CancellationToken cancellationToken);
}

// Phone verification can use this contract once a real SMS transport is selected.
public interface ISmsSender
{
    Task SendAsync(VerificationMessage message, CancellationToken cancellationToken);
}

public interface IAuthenticationStore
{
    Task<bool> TryCreateChallengeAsync(EmailChallenge challenge, AuthOptions limits, CancellationToken cancellationToken);
    Task SetDeliveredAsync(Guid id, bool delivered, CancellationToken cancellationToken);
    Task<ConfirmationResult> ConfirmAsync(Guid id, string codeHash, string bindingHash,
        DateTimeOffset now, DateTimeOffset sessionExpiresAt, int maxAttempts, CancellationToken cancellationToken);
    Task<AuthSession?> GetSessionAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);
    Task<AuthAccount?> GetAccountAsync(Guid id, CancellationToken cancellationToken);
    Task RevokeSessionAsync(Guid id, CancellationToken cancellationToken);
    Task CleanupAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public interface ICurrentUser
{
    Guid? Id { get; }
    AuthAccount? Account { get; }
}
