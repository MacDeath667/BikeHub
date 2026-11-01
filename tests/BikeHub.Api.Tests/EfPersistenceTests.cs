using BikeHub.Api.Authentication;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BikeHub.Api.Tests;

[CollectionDefinition("Postgres persistence")]
public sealed class PostgresPersistenceCollection { }

[Collection("Postgres persistence")]
public sealed class EfPersistenceTests
{
    [PostgresFact]
    public async Task Fresh_schema_migrates_repeatedly_and_matches_the_model()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        await new DatabaseMigrator(database).MigrateAsync(default);
        await using var context = database.CreateDbContext();
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.Bikes.ToListAsync());
        Assert.Empty(await context.Accounts.ToListAsync());
        Assert.Empty(await context.EmailChallenges.ToListAsync());
        Assert.Empty(await context.Sessions.ToListAsync());
    }

    [PostgresFact]
    public async Task Baseline_rollback_cannot_delete_adopted_tables()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        await using var context = database.CreateDbContext();
        var bike = new Bike { Id = Guid.NewGuid(), OwnerId = Guid.NewGuid(), Name = "Preserved bike" };
        context.Bikes.Add(bike);
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<NotSupportedException>(() => context.GetService<IMigrator>().MigrateAsync("0"));
        Assert.True(await context.Bikes.AnyAsync(x => x.Id == bike.Id));
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
    }

    [PostgresFact]
    public async Task Ef_adopts_populated_DbUp_schema_and_raw_sql_can_be_replayed_without_losing_data()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var assembly = typeof(EfPersistenceTests).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(x => x.Contains(".LegacyDbUp.")).Order())
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            await context.Database.ExecuteSqlRawAsync(await reader.ReadToEndAsync());
        }
        // The old migration journal belongs to DbUp and must remain intact during adoption.
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE schemaversions (schemaversionid integer PRIMARY KEY, scriptname text NOT NULL);
            INSERT INTO schemaversions VALUES (1, '003_email_authentication.sql');
            """);
        var now = DateTimeOffset.UtcNow;
        var account = new AuthAccountEntity
        {
            Id = Guid.NewGuid(),
            Email = "existing@example.test",
            DisplayName = "Existing rider",
            EmailConfirmedAt = now,
            PhoneE164 = "+79990000000",
            PhoneConfirmedAt = now,
            CreatedAt = now
        };
        var session = new AuthSessionEntity
        {
            Id = Guid.NewGuid(),
            Account = account,
            AccountId = account.Id,
            CreatedAt = now,
            ExpiresAt = now.AddDays(14)
        };
        var bike = new Bike { Id = Guid.NewGuid(), OwnerId = account.Id, Name = "Existing bike", Description = "Keep me" };
        var challenge = new EmailChallengeEntity
        {
            Id = Guid.NewGuid(),
            Email = account.Email,
            DisplayName = account.DisplayName,
            CodeHash = new string('A', 64),
            BindingHash = new string('B', 64),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(5),
            Attempts = 2,
            Delivered = true
        };
        context.AddRange(account, session, bike, challenge);
        await context.SaveChangesAsync();

        await new DatabaseMigrator(database).MigrateAsync(default);
        // Force the baseline SQL to run again against tables and indexes it has already adopted.
        await context.Database.ExecuteSqlRawAsync("""
            DELETE FROM "__EFMigrationsHistory";
            """);
        await new DatabaseMigrator(database).MigrateAsync(default);
        context.ChangeTracker.Clear();
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal("Keep me", (await context.Bikes.SingleAsync()).Description);
        Assert.Equal("+79990000000", (await context.Accounts.SingleAsync()).PhoneE164);
        Assert.Equal(2, (await context.EmailChallenges.SingleAsync()).Attempts);
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM schemaversions").SingleAsync());
        var store = new PostgresAuthenticationStore(database);
        Assert.Equal(account.Id, (await store.GetSessionAsync(session.Id, now, default))!.Account.Id);
        Assert.Equal(ConfirmationFailure.None, (await store.ConfirmAsync(challenge.Id, challenge.CodeHash,
            challenge.BindingHash, now, now.AddDays(14), 5, default)).Failure);
        Assert.Equal(1, await context.Accounts.CountAsync());
    }

    [PostgresFact]
    public async Task Partial_legacy_schema_is_completed_without_recreating_existing_tables()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var assembly = typeof(EfPersistenceTests).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(x => x.EndsWith("001_initial_schema.sql"));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        await context.Database.ExecuteSqlRawAsync(await reader.ReadToEndAsync());
        var bikeId = Guid.NewGuid();
        context.Bikes.Add(new Bike { Id = bikeId, OwnerId = Guid.NewGuid(), Name = "Partial schema bike" });
        await context.SaveChangesAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        Assert.True(await context.Bikes.AnyAsync(x => x.Id == bikeId));
        Assert.Empty(await context.Accounts.ToListAsync());
    }

    [PostgresFact]
    public async Task Bike_handlers_persist_and_read_using_ef()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        var ownerId = Guid.NewGuid();
        await new Application.Commands.AddBike.Handler(database).Handle(
            new("EF bike", "Persisted", ownerId), default);
        await using var context = database.CreateDbContext();
        var row = await context.Bikes.AsNoTracking().SingleAsync();
        Assert.NotEqual(default, row.CreatedAt);
        Assert.NotEqual(Guid.Empty, row.Id);
        var result = await new Application.Queries.GetBike.Handler(database).Handle(new(row.Id), default);
        Assert.Equal(ownerId, result.OwnerId);
        Assert.Equal("EF bike", result.Name);
        Assert.Equal("Persisted", result.Description);
    }

    [PostgresFact]
    public async Task Parallel_issuance_obeys_global_and_per_email_limits()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        var store = new PostgresAuthenticationStore(database);
        var now = DateTimeOffset.UtcNow;
        var hash = new string('A', 64);
        var budget = new AuthOptions { MaxPerDay = 3 };
        var issued = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => store.TryCreateChallengeAsync(
            new(Guid.NewGuid(), $"parallel-{i}@example.test", "Rider", hash, hash, now, now.AddMinutes(5)), budget, default)));
        Assert.Equal(3, issued.Count(x => x));
        await store.CleanupAsync(now.AddDays(2), default);
        var sameEmail = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => store.TryCreateChallengeAsync(
            new(Guid.NewGuid(), "same@example.test", "Rider", hash, hash, now, now.AddMinutes(5)), new AuthOptions(), default)));
        Assert.Equal(1, sameEmail.Count(x => x));
    }

    [PostgresFact]
    public async Task Distinct_valid_legacy_challenges_for_one_email_create_one_account()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        var now = DateTimeOffset.UtcNow;
        var hash = new string('A', 64);
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        await using (var context = database.CreateDbContext())
        {
            context.EmailChallenges.AddRange(ids.Select(id => new EmailChallengeEntity
            {
                Id = id,
                Email = "same@example.test",
                DisplayName = "Rider",
                CodeHash = hash,
                BindingHash = hash,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(5),
                Delivered = true
            }));
            await context.SaveChangesAsync();
        }
        var store = new PostgresAuthenticationStore(database);
        var results = await Task.WhenAll(ids.Select(id => store.ConfirmAsync(id, hash, hash, now, now.AddDays(14), 5, default)));
        Assert.All(results, r => Assert.Equal(ConfirmationFailure.None, r.Failure));
        Assert.Single(results.Select(r => r.Session!.Account.Id).Distinct());
        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.Accounts.CountAsync());
        Assert.Equal(ids.Length, await verify.Sessions.CountAsync());
    }
}
