using BikeHub.Api.Authentication;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Tests;

[Collection("Postgres persistence")]
public sealed class AuthenticationStoreTests
{
    [Fact]
    public Task Memory_store_obeys_the_authentication_contract() => VerifyContract(new InMemoryAuthenticationStore());

    [Fact]
    public async Task Postgres_migration_and_store_obey_the_authentication_contract()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        await context.Database.MigrateAsync();
        await VerifyContract(new PostgresAuthenticationStore(database));
    }

    [Fact]
    public Task Memory_store_enforces_challenge_purpose() => VerifyPurpose(new InMemoryAuthenticationStore());

    [Fact]
    public async Task Postgres_store_enforces_challenge_purpose()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        await VerifyPurpose(new PostgresAuthenticationStore(database));
    }

    private static async Task VerifyPurpose(IAuthenticationStore store)
    {
        var now = DateTimeOffset.UtcNow;
        var hash = new string('A', 64);
        var email = "flow@example.test";
        async Task<ConfirmationResult> Confirm(AuthenticationPurpose purpose, DateTimeOffset time, string name)
        {
            var challenge = new EmailChallenge(Guid.NewGuid(), email, name, hash, hash,
                time, time.AddMinutes(5), Purpose: purpose);
            Assert.True(await store.TryCreateChallengeAsync(challenge, new(), default));
            await store.SetDeliveredAsync(challenge.Id, true, default);
            return await store.ConfirmAsync(challenge.Id, hash, hash, time, time.AddDays(14), 5, default);
        }
        Assert.Equal(ConfirmationFailure.AccountNotFound, (await Confirm(AuthenticationPurpose.Login, now, "Ignored")).Failure);
        Assert.Null(await store.FindByEmailAsync(email, default));
        var registered = (await Confirm(AuthenticationPurpose.Registration, now.AddMinutes(2), "Original")).Session!;
        // Bypass the HTTP precheck: confirmation must independently reject an existing account.
        Assert.Equal(ConfirmationFailure.AccountExists,
            (await Confirm(AuthenticationPurpose.Registration, now.AddMinutes(4), "Replacement")).Failure);
        var login = (await Confirm(AuthenticationPurpose.Login, now.AddMinutes(6), "Replacement")).Session!;
        Assert.Equal(registered.Account.Id, login.Account.Id);
        Assert.Equal("Original", login.Account.DisplayName);
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

        var second = Make(first.Email, now.AddSeconds(61), binding) with { DisplayName = "Другое имя" };
        Assert.True(await store.TryCreateChallengeAsync(second, limits, default));
        await store.SetDeliveredAsync(second.Id, true, default);
        var returning = (await Confirm(second, second.CreatedAt)).Session!;
        Assert.Equal(session.Account.Id, returning.Account.Id);
        Assert.Equal(session.Account.DisplayName, returning.Account.DisplayName);
        Assert.Equal(session.Account.DisplayName, (await store.GetAccountAsync(returning.Account.Id, default))!.DisplayName);
        Assert.False(await store.TryCreateChallengeAsync(Make(first.Email, now.AddMinutes(3), binding), limits, default));

        var third = Make("two@example.test", now.AddMinutes(3), binding);
        Assert.True(await store.TryCreateChallengeAsync(third, limits, default));
        await store.SetDeliveredAsync(third.Id, false, default);
        Assert.Null((await Confirm(third, third.CreatedAt)).Session);
        var fourth = Make("three@example.test", now.AddMinutes(3), binding);
        Assert.True(await store.TryCreateChallengeAsync(fourth, limits, default));
        await store.SetDeliveredAsync(fourth.Id, true, default);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.Equal(attempt == 5 ? ConfirmationFailure.TooManyAttempts : ConfirmationFailure.Invalid,
                (await Confirm(fourth, fourth.CreatedAt, code: new string('D', 64))).Failure);
        }

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
