namespace BikeHub.Api.Authentication;

// Development/test implementation; production uses the transactional PostgreSQL store.
public sealed class InMemoryAuthenticationStore : IAuthenticationStore
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, EmailChallenge> challenges = [];
    private readonly Dictionary<string, AuthAccount> accounts = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, AuthSession> sessions = [];

    public Task<bool> TryCreateChallengeAsync(EmailChallenge challenge, AuthOptions limits, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var recent = challenges.Values.Where(c => c.Email == challenge.Email).ToList();
            if (recent.Any(c => c.CreatedAt > challenge.CreatedAt.AddSeconds(-limits.ResendSeconds)) ||
                recent.Count(c => c.CreatedAt > challenge.CreatedAt.AddHours(-1)) >= limits.MaxPerEmailHour ||
                challenges.Values.Count(c => c.CreatedAt > challenge.CreatedAt.AddDays(-1)) >= limits.MaxPerDay)
            {
                return Task.FromResult(false);
            }

            foreach (var previous in challenges.Values.Where(c => !c.Consumed &&
                         (c.Email == challenge.Email || c.BindingHash == challenge.BindingHash)).ToList())
            {
                challenges[previous.Id] = previous with { Consumed = true };
            }

            challenges.Add(challenge.Id, challenge);
            return Task.FromResult(true);
        }
    }

    public Task SetDeliveredAsync(Guid id, bool delivered, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (challenges.TryGetValue(id, out var value))
            {
                challenges[id] = value with { Delivered = delivered, Consumed = value.Consumed || !delivered };
            }

            return Task.CompletedTask;
        }
    }

    public Task<ConfirmationResult> ConfirmAsync(Guid id, string codeHash, string bindingHash,
        DateTimeOffset now, DateTimeOffset sessionExpiresAt, int maxAttempts, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!challenges.TryGetValue(id, out var value) || value.Consumed || !value.Delivered ||
                value.ExpiresAt <= now || !OtpSecurity.EqualsHash(value.BindingHash, bindingHash))
            {
                return Task.FromResult(new ConfirmationResult(null, ConfirmationFailure.Invalid));
            }

            if (value.Attempts >= maxAttempts)
            {
                return Task.FromResult(new ConfirmationResult(null, ConfirmationFailure.TooManyAttempts));
            }

            value = value with { Attempts = value.Attempts + 1 };
            if (!OtpSecurity.EqualsHash(value.CodeHash, codeHash))
            {
                challenges[id] = value;
                return Task.FromResult(new ConfirmationResult(null,
                    value.Attempts >= maxAttempts ? ConfirmationFailure.TooManyAttempts : ConfirmationFailure.Invalid));
            }
            challenges[id] = value with { Consumed = true };
            if (!accounts.TryGetValue(value.Email, out var account))
            {
                account = new(Guid.CreateVersion7(), value.Email, value.DisplayName, now);
                accounts.Add(account.Email, account);
            }
            var session = new AuthSession(Guid.NewGuid(), account, sessionExpiresAt);
            sessions.Add(session.Id, session);
            return Task.FromResult(new ConfirmationResult(session, ConfirmationFailure.None));
        }
    }

    public Task<AuthSession?> GetSessionAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(sessions.TryGetValue(id, out var value) && value.ExpiresAt > now ? value : null);
        }
    }

    public Task<AuthAccount?> GetAccountAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(accounts.Values.FirstOrDefault(a => a.Id == id));
        }
    }

    public Task RevokeSessionAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            sessions.Remove(id);
        }

        return Task.CompletedTask;
    }

    public Task CleanupAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            foreach (var id in challenges.Values.Where(c => c.CreatedAt < now.AddDays(-1)).Select(c => c.Id).ToList())
            {
                challenges.Remove(id);
            }

            foreach (var id in sessions.Values.Where(s => s.ExpiresAt <= now).Select(s => s.Id).ToList())
            {
                sessions.Remove(id);
            }
        }
        return Task.CompletedTask;
    }
}
