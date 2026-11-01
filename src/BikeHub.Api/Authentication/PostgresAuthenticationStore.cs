using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Authentication;

public sealed class PostgresAuthenticationStore(IDbContextFactory<BikeHubDbContext> contexts) : IAuthenticationStore
{
    private const string AcquireDeliveryLockSql = "SELECT pg_advisory_xact_lock(743802319)";
    // Two-key advisory locks use a separate namespace from the global one-key delivery lock.
    private const string AcquireAccountLockSql = "SELECT pg_advisory_xact_lock(1112099653, hashtext({0}))";
    private const string SelectChallengeForUpdateSql = "SELECT * FROM auth_email_challenges WHERE id = {0} FOR UPDATE";

    public async Task<bool> TryCreateChallengeAsync(EmailChallenge value, AuthOptions limits, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(AcquireDeliveryLockSql, cancellationToken);
        var cooldown = value.CreatedAt.AddSeconds(-limits.ResendSeconds);
        var hour = value.CreatedAt.AddHours(-1);
        var day = value.CreatedAt.AddDays(-1);
        if (await context.EmailChallenges.AnyAsync(c => c.Email == value.Email && c.CreatedAt > cooldown, cancellationToken) ||
            await context.EmailChallenges.CountAsync(c => c.Email == value.Email && c.CreatedAt > hour, cancellationToken) >= limits.MaxPerEmailHour ||
            await context.EmailChallenges.CountAsync(c => c.CreatedAt > day, cancellationToken) >= limits.MaxPerDay)
        {
            return false;
        }
        await context.EmailChallenges.Where(c => !c.Consumed && (c.Email == value.Email || c.BindingHash == value.BindingHash))
            .ExecuteUpdateAsync(update => update.SetProperty(c => c.Consumed, true), cancellationToken);
        context.EmailChallenges.Add(new EmailChallengeEntity
        {
            Id = value.Id,
            Email = value.Email,
            DisplayName = value.DisplayName,
            CodeHash = value.CodeHash,
            BindingHash = value.BindingHash,
            CreatedAt = value.CreatedAt,
            ExpiresAt = value.ExpiresAt
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task SetDeliveredAsync(Guid id, bool delivered, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await context.EmailChallenges.Where(c => c.Id == id).ExecuteUpdateAsync(update => update
            .SetProperty(c => c.Delivered, delivered)
            .SetProperty(c => c.Consumed, c => c.Consumed || !delivered), cancellationToken);
    }

    public async Task<ConfirmationResult> ConfirmAsync(Guid id, string codeHash, string bindingHash,
        DateTimeOffset now, DateTimeOffset sessionExpiresAt, int maxAttempts, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var value = await context.EmailChallenges.FromSqlRaw(SelectChallengeForUpdateSql, id)
            .SingleOrDefaultAsync(cancellationToken);
        if (value is null || value.Consumed || !value.Delivered || value.ExpiresAt <= now ||
            !OtpSecurity.EqualsHash(value.BindingHash, bindingHash))
        {
            return new(null, ConfirmationFailure.Invalid);
        }
        if (value.Attempts >= maxAttempts)
        {
            return new(null, ConfirmationFailure.TooManyAttempts);
        }
        var valid = OtpSecurity.EqualsHash(value.CodeHash, codeHash);
        value.Attempts++;
        value.Consumed = valid;
        await context.SaveChangesAsync(cancellationToken);
        if (!valid)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(null, value.Attempts >= maxAttempts ? ConfirmationFailure.TooManyAttempts : ConfirmationFailure.Invalid);
        }

        // Separate valid challenges for the same email must converge on a single account.
        await context.Database.ExecuteSqlRawAsync(AcquireAccountLockSql, new object[] { value.Email }, cancellationToken);
        var account = await context.Accounts.SingleOrDefaultAsync(a => a.Email == value.Email, cancellationToken);
        if (account is null)
        {
            account = new AuthAccountEntity
            {
                Id = Guid.CreateVersion7(),
                Email = value.Email,
                DisplayName = value.DisplayName,
                EmailConfirmedAt = now,
                CreatedAt = now
            };
            context.Accounts.Add(account);
        }
        var session = new AuthSessionEntity
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Account = account,
            CreatedAt = now,
            ExpiresAt = sessionExpiresAt
        };
        context.Sessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(new AuthSession(session.Id, MapAccount(account), session.ExpiresAt), ConfirmationFailure.None);
    }

    public async Task<AuthSession?> GetSessionAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var session = await context.Sessions.AsNoTracking().Include(s => s.Account)
            .SingleOrDefaultAsync(s => s.Id == id && s.ExpiresAt > now, cancellationToken);
        return session is null ? null : new(session.Id, MapAccount(session.Account), session.ExpiresAt);
    }

    public async Task<AuthAccount?> GetAccountAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var account = await context.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        return account is null ? null : MapAccount(account);
    }

    public async Task RevokeSessionAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await context.Sessions.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task CleanupAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var day = now.AddDays(-1);
        await context.EmailChallenges.Where(c => c.CreatedAt < day).ExecuteDeleteAsync(cancellationToken);
        await context.Sessions.Where(s => s.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static AuthAccount MapAccount(AuthAccountEntity value) =>
        new(value.Id, value.Email, value.DisplayName, value.EmailConfirmedAt);
}
