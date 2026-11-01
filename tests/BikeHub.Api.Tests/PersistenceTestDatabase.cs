using BikeHub.Dal;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BikeHub.Api.Tests;

public sealed class PersistenceTestDatabase : IDbContextFactory<BikeHubDbContext>, IAsyncDisposable
{
    private readonly string schema = "ef_test_" + Guid.NewGuid().ToString("N");
    private readonly NpgsqlDataSource admin;
    private readonly DbContextOptions<BikeHubDbContext> options;

    private PersistenceTestDatabase(string connectionString)
    {
        admin = NpgsqlDataSource.Create(connectionString);
        var scoped = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString;
        options = new DbContextOptionsBuilder<BikeHubDbContext>().UseNpgsql(scoped).Options;
    }

    public static async Task<PersistenceTestDatabase> CreateAsync()
    {
        var database = new PersistenceTestDatabase(BikeHub.Testing.PostgresFixture.ConnectionString);
        try
        {
            await using var create = database.admin.CreateCommand($"CREATE SCHEMA {database.schema}");
            await create.ExecuteNonQueryAsync();
            return database;
        }
        catch
        {
            await database.admin.DisposeAsync();
            throw;
        }
    }

    public BikeHubDbContext CreateDbContext() => new(options);

    public Task<BikeHubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var drop = admin.CreateCommand($"DROP SCHEMA {schema} CASCADE");
            await drop.ExecuteNonQueryAsync();
        }
        finally
        {
            await admin.DisposeAsync();
        }
    }
}
