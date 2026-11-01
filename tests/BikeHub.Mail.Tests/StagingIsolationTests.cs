using BikeHub.Api.Authentication;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using BikeHub.Mail.Contracts;
using BikeHub.Mail.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Mail.Tests;

[Collection("Mail persistence")]
public sealed class StagingIsolationTests
{
    [Fact]
    public async Task Production_and_staging_share_a_database_with_independent_migrations_auth_and_mail()
    {
        await using var database = await MailTestDatabase.CreateAsync();
        var production = new ApiFactory(database.ConnectionString, DatabaseSchema.Default);
        var staging = new ApiFactory(database.ConnectionString, "staging");
        await new DatabaseMigrator(production).MigrateAsync(default);
        var account = await SignInAsync(production, "Production rider");
        await using var prodContext = production.CreateDbContext();
        prodContext.Bikes.Add(new Bike { Name = "Production bike", OwnerId = account.Account.Id });
        await prodContext.SaveChangesAsync();
        var prodMigrations = (await prodContext.Database.GetAppliedMigrationsAsync()).ToArray();
        var prodObjects = await PublicObjectsAsync(prodContext);
        var message = new SendEmailRequest(Guid.NewGuid(), "recipient@example.test", "Subject", "Production body");
        var prodQueue = new MailQueue(database, TimeProvider.System);
        await prodQueue.EnqueueAsync(message, default);

        await new DatabaseMigrator(staging).MigrateAsync(default);
        await new DatabaseMigrator(staging).MigrateAsync(default);
        var stageMail = new MailFactory(database.ConnectionString, "staging");
        await using (var context = stageMail.CreateDbContext())
        {
            await MailDatabase.MigrateAsync(context);
            await MailDatabase.MigrateAsync(context);
            Assert.False(context.Database.HasPendingModelChanges());
            Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
        }
        await using var stageContext = staging.CreateDbContext();
        Assert.Empty(await stageContext.Bikes.ToListAsync());
        Assert.Empty(await stageContext.Accounts.ToListAsync());
        Assert.False(stageContext.Database.HasPendingModelChanges());
        Assert.Equal(prodMigrations, await stageContext.Database.GetAppliedMigrationsAsync());
        var stageStore = new PostgresAuthenticationStore(staging);
        Assert.Null(await stageStore.GetSessionAsync(account.Id, DateTimeOffset.UtcNow, default));
        var stageAccount = await SignInAsync(staging, "Staging rider");
        Assert.NotEqual(account.Account.Id, stageAccount.Account.Id);
        Assert.Equal("Staging rider", stageAccount.Account.DisplayName);
        stageContext.Bikes.Add(new Bike { Name = "Staging bike", OwnerId = stageAccount.Account.Id });
        await stageContext.SaveChangesAsync();

        var stageQueue = new MailQueue(stageMail, TimeProvider.System);
        Assert.Null(await stageQueue.FindAsync(message.MessageId, default));
        await stageQueue.EnqueueAsync(message with { TextBody = "Staging body" }, default);
        await stageQueue.RejectKafkaAsync("staging-topic", 0, 1, "invalid_payload", default);
        Assert.Equal("Staging body", (await stageQueue.ClaimAsync(default))!.TextBody);
        Assert.Equal("Production body", (await prodQueue.ClaimAsync(default))!.TextBody);
        await using var prodMailContext = database.CreateDbContext();
        Assert.Empty(await prodMailContext.KafkaRejections.ToListAsync());
        await stageStore.CleanupAsync(DateTimeOffset.UtcNow.AddDays(30), default);
        Assert.NotNull(await new PostgresAuthenticationStore(production).GetSessionAsync(account.Id, DateTimeOffset.UtcNow, default));
        Assert.Equal("Production bike", (await prodContext.Bikes.SingleAsync()).Name);
        Assert.Equal("Production rider", (await prodContext.Accounts.SingleAsync()).DisplayName);
        Assert.Equal(prodMigrations, await prodContext.Database.GetAppliedMigrationsAsync());
        Assert.Equal(prodObjects, await PublicObjectsAsync(prodContext));

        // No public fallback: a missing staging table must fail instead of exposing production sessions.
        await stageContext.Database.ExecuteSqlRawAsync("DROP TABLE auth_sessions");
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => stageStore.GetSessionAsync(account.Id, DateTimeOffset.UtcNow, default));
    }

    [Theory]
    [InlineData("staging,public")]
    [InlineData("public;DROP SCHEMA public")]
    [InlineData("")]
    public void Schema_configuration_rejects_fallbacks_and_invalid_identifiers(string schema)
    {
        Assert.Throws<InvalidOperationException>(() => DatabaseSchema.Configure(new DbContextOptionsBuilder<BikeHubDbContext>(), "Host=localhost", schema));
        Assert.Throws<InvalidOperationException>(() => MailDatabase.Configure(new DbContextOptionsBuilder<MailDbContext>(), "Host=localhost", schema));
    }

    [Fact]
    public async Task Existing_mail_tables_and_migration_journal_are_preserved_during_the_mapping_upgrade()
    {
        await using var database = await MailTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        // Recreate the journal state of the deployed baseline, with an already queued message.
        await context.Database.ExecuteSqlRawAsync("""
            DELETE FROM mail."__EFMigrationsHistory" WHERE "MigrationId" LIKE '%_UseConnectionSchema';
            """);
        var request = new SendEmailRequest(Guid.NewGuid(), "existing@example.test", "Subject", "Preserved body");
        var queue = new MailQueue(database, TimeProvider.System);
        await queue.EnqueueAsync(request, default);
        var before = await context.Database.SqlQueryRaw<string>("SELECT 'mail.messages'::regclass::oid::text AS \"Value\"").SingleAsync();
        await MailDatabase.MigrateAsync(context);
        Assert.Equal(before, await context.Database.SqlQueryRaw<string>("SELECT 'mail.messages'::regclass::oid::text AS \"Value\"").SingleAsync());
        Assert.Equal("Preserved body", (await queue.ClaimAsync(default))!.TextBody);
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name IN ('messages', 'kafka_rejections', '__EFMigrationsHistory')
            """).SingleAsync());
    }

    private static async Task<string[]> PublicObjectsAsync(BikeHubDbContext context) => await context.Database.SqlQueryRaw<string>("""
        SELECT c.oid::text || ':' || c.relname AS "Value" FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' ORDER BY c.oid
        """).ToArrayAsync();

    private static async Task<AuthSession> SignInAsync(ApiFactory factory, string name)
    {
        var store = new PostgresAuthenticationStore(factory);
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var hash = new string('A', 64);
        Assert.True(await store.TryCreateChallengeAsync(new(id, "rider@example.test", name, hash, hash, now, now.AddMinutes(5)), new(), default));
        await store.SetDeliveredAsync(id, true, default);
        return (await store.ConfirmAsync(id, hash, hash, now, now.AddDays(14), 5, default)).Session!;
    }

    private sealed class ApiFactory(string connectionString, string schema) : IDbContextFactory<BikeHubDbContext>
    {
        public BikeHubDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<BikeHubDbContext>();
            DatabaseSchema.Configure(options, connectionString, schema);
            return new(options.Options);
        }
    }

    private sealed class MailFactory(string connectionString, string schema) : IDbContextFactory<MailDbContext>
    {
        public MailDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<MailDbContext>();
            MailDatabase.Configure(options, connectionString, schema);
            return new(options.Options);
        }
    }
}
