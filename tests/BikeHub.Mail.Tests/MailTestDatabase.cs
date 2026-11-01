using BikeHub.Mail.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BikeHub.Mail.Tests;

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan value) => now += value;
}

[CollectionDefinition("Mail persistence")]
public sealed class MailPersistenceCollection : ICollectionFixture<BikeHub.Testing.PostgresFixture> { }

public sealed class MailTestDatabase : IDbContextFactory<MailDbContext>, IAsyncDisposable
{
    private readonly string name = "mail_test_" + Guid.NewGuid().ToString("N");
    private readonly string admin = BikeHub.Testing.PostgresFixture.ConnectionString;
    public string ConnectionString { get; private set; } = string.Empty;

    public static async Task<MailTestDatabase> CreateAsync()
    {
        var database = new MailTestDatabase();
        await using var connection = new NpgsqlConnection(database.admin);
        await connection.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {database.name}", connection);
        await create.ExecuteNonQueryAsync();
        database.ConnectionString = new NpgsqlConnectionStringBuilder(database.admin) { Database = database.name }.ConnectionString;
        await using var context = database.CreateDbContext();
        await context.Database.MigrateAsync();
        return database;
    }

    public MailDbContext CreateDbContext() => new(new DbContextOptionsBuilder<MailDbContext>().UseNpgsql(ConnectionString,
        postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "mail")).Options);

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }
}
