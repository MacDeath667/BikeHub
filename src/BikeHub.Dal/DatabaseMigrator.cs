using DbUp;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BikeHub.Dal;

public class DatabaseMigrator(IConfiguration configuration)
{
    public Task MigrateAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("BikeHub").Protect();

        var resources = typeof(DatabaseMigrator)
            .Assembly
            .GetManifestResourceNames();

        foreach (var resource in resources)
        {
            Console.WriteLine($"RESOURCE: {resource}");
        }

        var upgrader = DeployChanges
            .To.PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(DatabaseMigrator).Assembly,
                script => script.StartsWith(
                    "BikeHub.Dal"))
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();

        return !result.Successful
            ? throw new InvalidOperationException("Database migration failed.", result.Error)
            : Task.CompletedTask;
    }
}

public sealed class DatabaseBootstrapper(IConfiguration configuration)
{
    public async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("BikeHub")
                               ?? throw new InvalidOperationException(
                                   "Connection string 'BikeHub' is not configured.");

        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        var databaseName = builder.Database;

        builder.Database = "postgres";

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT 1
                              FROM pg_database
                              WHERE datname = @databaseName;
                              """;

        command.Parameters.AddWithValue("databaseName", databaseName);

        var exists = await command.ExecuteScalarAsync(cancellationToken) is not null;

        if (exists)
            return;

        command.CommandText = $"""CREATE DATABASE "{databaseName}";""";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}