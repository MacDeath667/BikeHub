using Npgsql;

namespace BikeHub.Api.Authentication;

public sealed class PostgresAuthenticationStore(string connectionString) : IAuthenticationStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource source = NpgsqlDataSource.Create(connectionString);

    public async Task<bool> TryCreateChallengeAsync(EmailChallenge value, AuthOptions limits, CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // Serialize issuance, including the global delivery budget, across API replicas.
        await using (var mutex = new NpgsqlCommand("SELECT pg_advisory_xact_lock(743802319)", connection, transaction))
            await mutex.ExecuteNonQueryAsync(cancellationToken);
        const string sql = """
            SELECT NOT EXISTS (
                SELECT 1 FROM auth_email_challenges WHERE email = @email AND created_at > @cooldown
            ) AND (
                SELECT count(*) FROM auth_email_challenges WHERE email = @email AND created_at > @hour
            ) < @per_email AND (
                SELECT count(*) FROM auth_email_challenges WHERE created_at > @day
            ) < @per_day
            """;
        await using (var check = new NpgsqlCommand(sql, connection, transaction))
        {
            check.Parameters.AddWithValue("email", value.Email);
            check.Parameters.AddWithValue("cooldown", value.CreatedAt.AddSeconds(-limits.ResendSeconds));
            check.Parameters.AddWithValue("hour", value.CreatedAt.AddHours(-1));
            check.Parameters.AddWithValue("day", value.CreatedAt.AddDays(-1));
            check.Parameters.AddWithValue("per_email", limits.MaxPerEmailHour);
            check.Parameters.AddWithValue("per_day", limits.MaxPerDay);
            if (await check.ExecuteScalarAsync(cancellationToken) is not true) return false;
        }
        await using (var invalidate = new NpgsqlCommand("""
            UPDATE auth_email_challenges SET consumed = true
            WHERE NOT consumed AND (email = @email OR binding_hash = @binding)
            """, connection, transaction))
        {
            invalidate.Parameters.AddWithValue("email", value.Email);
            invalidate.Parameters.AddWithValue("binding", value.BindingHash);
            await invalidate.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO auth_email_challenges
                (id, email, display_name, code_hash, binding_hash, created_at, expires_at)
            VALUES (@id, @email, @name, @hash, @binding, @created, @expires)
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("id", value.Id);
            insert.Parameters.AddWithValue("email", value.Email);
            insert.Parameters.AddWithValue("name", value.DisplayName);
            insert.Parameters.AddWithValue("hash", value.CodeHash);
            insert.Parameters.AddWithValue("binding", value.BindingHash);
            insert.Parameters.AddWithValue("created", value.CreatedAt);
            insert.Parameters.AddWithValue("expires", value.ExpiresAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task SetDeliveredAsync(Guid id, bool delivered, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            UPDATE auth_email_challenges SET delivered = @delivered, consumed = consumed OR NOT @delivered WHERE id = @id
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("delivered", delivered);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ConfirmationResult> ConfirmAsync(Guid id, string codeHash, string bindingHash,
        DateTimeOffset now, DateTimeOffset sessionExpiresAt, int maxAttempts, CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        EmailChallenge? value;
        await using (var select = new NpgsqlCommand("""
            SELECT id,email,display_name,code_hash,binding_hash,created_at,expires_at,attempts,delivered,consumed
            FROM auth_email_challenges WHERE id = @id FOR UPDATE
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("id", id);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            value = await reader.ReadAsync(cancellationToken)
                ? new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetInt32(7),
                    reader.GetBoolean(8), reader.GetBoolean(9)) : null;
        }
        if (value is null || value.Consumed || !value.Delivered || value.ExpiresAt <= now ||
            !OtpSecurity.EqualsHash(value.BindingHash, bindingHash))
            return new(null, ConfirmationFailure.Invalid);
        if (value.Attempts >= maxAttempts) return new(null, ConfirmationFailure.TooManyAttempts);
        var valid = OtpSecurity.EqualsHash(value.CodeHash, codeHash);
        await using (var update = new NpgsqlCommand("""
            UPDATE auth_email_challenges SET attempts = attempts + 1, consumed = @valid WHERE id = @id
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("id", id);
            update.Parameters.AddWithValue("valid", valid);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        if (!valid)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(null, value.Attempts + 1 >= maxAttempts ? ConfirmationFailure.TooManyAttempts : ConfirmationFailure.Invalid);
        }
        await using (var account = new NpgsqlCommand("""
            INSERT INTO auth_accounts(id,email,display_name,email_confirmed_at,created_at)
            VALUES (@id,@email,@name,@now,@now) ON CONFLICT(email) DO NOTHING
            """, connection, transaction))
        {
            account.Parameters.AddWithValue("id", Guid.CreateVersion7());
            account.Parameters.AddWithValue("email", value.Email);
            account.Parameters.AddWithValue("name", value.DisplayName);
            account.Parameters.AddWithValue("now", now);
            await account.ExecuteNonQueryAsync(cancellationToken);
        }
        AuthAccount user;
        await using (var account = new NpgsqlCommand(
            "SELECT id,email,display_name,email_confirmed_at FROM auth_accounts WHERE email = @email", connection, transaction))
        {
            account.Parameters.AddWithValue("email", value.Email);
            await using var reader = await account.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            user = ReadAccount(reader, 0);
        }
        var session = new AuthSession(Guid.NewGuid(), user, sessionExpiresAt);
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO auth_sessions(id,account_id,created_at,expires_at) VALUES (@id,@account,@now,@expires)
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("id", session.Id);
            insert.Parameters.AddWithValue("account", user.Id);
            insert.Parameters.AddWithValue("now", now);
            insert.Parameters.AddWithValue("expires", sessionExpiresAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new(session, ConfirmationFailure.None);
    }

    public async Task<AuthSession?> GetSessionAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            SELECT s.id,s.expires_at,a.id,a.email,a.display_name,a.email_confirmed_at
            FROM auth_sessions s JOIN auth_accounts a ON a.id = s.account_id
            WHERE s.id = @id AND s.expires_at > @now
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("now", now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetGuid(0), ReadAccount(reader, 2), reader.GetFieldValue<DateTimeOffset>(1)) : null;
    }

    public async Task<AuthAccount?> GetAccountAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("SELECT id,email,display_name,email_confirmed_at FROM auth_accounts WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAccount(reader, 0) : null;
    }

    public async Task RevokeSessionAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("DELETE FROM auth_sessions WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CleanupAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            DELETE FROM auth_email_challenges WHERE created_at < @day;
            DELETE FROM auth_sessions WHERE expires_at <= @now;
            """);
        command.Parameters.AddWithValue("day", now.AddDays(-1));
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static AuthAccount ReadAccount(NpgsqlDataReader reader, int offset) => new(
        reader.GetGuid(offset), reader.GetString(offset + 1), reader.GetString(offset + 2),
        reader.GetFieldValue<DateTimeOffset>(offset + 3));

    public ValueTask DisposeAsync() => source.DisposeAsync();
}
