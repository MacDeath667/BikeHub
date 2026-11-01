using BikeHub.Api.Authentication;
using BikeHub.Dal;
using Npgsql;

namespace BikeHub.Api.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BIKEHUB_TEST_POSTGRES")))
            Skip = "Run python3 scripts/test-auth-postgres.py to start an isolated PostgreSQL container.";
    }
}

public sealed class AuthenticationStoreTests
{
    [Fact]
    public Task Memory_store_obeys_the_authentication_contract() => VerifyContract(new InMemoryAuthenticationStore());

    [PostgresFact]
    public async Task Postgres_migration_and_store_obey_the_authentication_contract()
    {
        var connectionString = Environment.GetEnvironmentVariable("BIKEHUB_TEST_POSTGRES")!;
        var schema = "auth_test_" + Guid.NewGuid().ToString("N");
        await using var admin = NpgsqlDataSource.Create(connectionString);
        await using (var create = admin.CreateCommand($"CREATE SCHEMA {schema}")) await create.ExecuteNonQueryAsync();
        try
        {
            var scoped = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString;
            await using (var source = NpgsqlDataSource.Create(scoped))
            {
                var assembly = typeof(DatabaseMigrator).Assembly;
                var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("003_email_authentication.sql"));
                using var sql = new StreamReader(assembly.GetManifestResourceStream(name)!);
                await using var migrate = source.CreateCommand(await sql.ReadToEndAsync());
                await migrate.ExecuteNonQueryAsync();
            }
            await using (var store = new PostgresAuthenticationStore(scoped)) await VerifyContract(store);
        }
        finally
        {
            await using var drop = admin.CreateCommand($"DROP SCHEMA {schema} CASCADE");
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task VerifyContract(IAuthenticationStore store)
    {
        var now = DateTimeOffset.UtcNow;
        var limits = new AuthOptions { MaxPerEmailHour = 2, MaxPerDay = 4 };
        var hash = new string('A', 64);
        var binding = new string('B', 64);
        EmailChallenge Make(string email, DateTimeOffset time, string browser) =>
            new(Guid.NewGuid(), email, "Участник", hash, browser, time, time.AddMinutes(5));
        Task<ConfirmationResult> Confirm(EmailChallenge value, DateTimeOffset time, string? code = null, string? browser = null) =>
            store.ConfirmAsync(value.Id, code ?? hash, browser ?? value.BindingHash, time, time.AddDays(14), 5, default);

        var first = Make("one@example.test", now, binding);
        Assert.True(await store.TryCreateChallengeAsync(first, limits, default));
        Assert.Null((await Confirm(first, now)).Session); // Delivery has not succeeded yet.
        await store.SetDeliveredAsync(first.Id, true, default);
        Assert.Null((await Confirm(first, now, browser: new string('C', 64))).Session);
        var race = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Confirm(first, now)));
        var session = Assert.Single(race, r => r.Session is not null).Session!;
        Assert.Equal(ConfirmationFailure.None, race.Single(r => r.Session is not null).Failure);
        Assert.Equal("one@example.test", (await store.GetAccountAsync(session.Account.Id, default))!.Email);
        Assert.NotNull(await store.GetSessionAsync(session.Id, now, default));
        Assert.Null(await store.GetSessionAsync(session.Id, now.AddDays(15), default));
        await store.RevokeSessionAsync(session.Id, default);
        Assert.Null(await store.GetSessionAsync(session.Id, now, default));
        Assert.False(await store.TryCreateChallengeAsync(Make(first.Email, now.AddSeconds(30), binding), limits, default));

        var second = Make(first.Email, now.AddSeconds(61), binding);
        Assert.True(await store.TryCreateChallengeAsync(second, limits, default));
        await store.SetDeliveredAsync(second.Id, true, default);
        var returning = (await Confirm(second, second.CreatedAt)).Session!;
        Assert.Equal(session.Account.Id, returning.Account.Id);
        Assert.False(await store.TryCreateChallengeAsync(Make(first.Email, now.AddMinutes(3), binding), limits, default));

        var third = Make("two@example.test", now.AddMinutes(3), binding);
        Assert.True(await store.TryCreateChallengeAsync(third, limits, default));
        await store.SetDeliveredAsync(third.Id, false, default);
        Assert.Null((await Confirm(third, third.CreatedAt)).Session);
        var fourth = Make("three@example.test", now.AddMinutes(3), binding);
        Assert.True(await store.TryCreateChallengeAsync(fourth, limits, default));
        await store.SetDeliveredAsync(fourth.Id, true, default);
        for (var attempt = 1; attempt <= 5; attempt++)
            Assert.Equal(attempt == 5 ? ConfirmationFailure.TooManyAttempts : ConfirmationFailure.Invalid,
                (await Confirm(fourth, fourth.CreatedAt, code: new string('D', 64))).Failure);
        Assert.Equal(ConfirmationFailure.TooManyAttempts, (await Confirm(fourth, fourth.CreatedAt)).Failure);
        Assert.False(await store.TryCreateChallengeAsync(Make("four@example.test", now.AddMinutes(4), binding), limits, default));

        await store.CleanupAsync(now.AddDays(2), default);
        var expired = Make("expired@example.test", now.AddDays(2), binding);
        Assert.True(await store.TryCreateChallengeAsync(expired, limits, default));
        await store.SetDeliveredAsync(expired.Id, true, default);
        Assert.Null((await Confirm(expired, expired.ExpiresAt)).Session);
        var replacement = Make("replacement@example.test", expired.CreatedAt.AddSeconds(61), binding);
        Assert.True(await store.TryCreateChallengeAsync(replacement, limits, default));
        Assert.Null((await Confirm(expired, expired.CreatedAt.AddSeconds(62))).Session); // Same browser invalidates older code.
    }
}
