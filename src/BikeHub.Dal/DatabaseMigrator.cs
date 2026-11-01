using DbUp;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BikeHub.Dal;

public class DatabaseMigrator(IConfiguration configuration)
{
    private const string ResourceLogFormat = "RESOURCE: {0}";
    private const string MigrationResourcePrefix = "BikeHub.Dal";
    private const string MigrationFailedMessage = "Database migration failed.";

    public Task MigrateAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString(Constants.ConnectionStringName).Protect();

        var resources = typeof(DatabaseMigrator)
            .Assembly
            .GetManifestResourceNames();

        foreach (var resource in resources)
        {
            Console.WriteLine(string.Format(ResourceLogFormat, resource));
        }

        var upgrader = DeployChanges
            .To.PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(DatabaseMigrator).Assembly,
                script => script.StartsWith(
                    MigrationResourcePrefix))
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();

        return !result.Successful
            ? throw new InvalidOperationException(MigrationFailedMessage, result.Error)
            : Task.CompletedTask;
    }
}

public sealed class DatabaseBootstrapper(IConfiguration configuration)
{
    private const string MissingConnectionStringMessage = "Connection string 'BikeHub' is not configured.";
    private const string MaintenanceDatabase = "postgres";
    private const string DatabaseExistsSql = """
        SELECT 1
        FROM pg_database
        WHERE datname = @databaseName;
        """;
    private const string CreateDatabaseSql = """CREATE DATABASE "{0}";""";

    public async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString(Constants.ConnectionStringName)
                               ?? throw new InvalidOperationException(
                                   MissingConnectionStringMessage);

        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        var databaseName = builder.Database;

        builder.Database = MaintenanceDatabase;

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = DatabaseExistsSql;

        command.Parameters.AddWithValue("databaseName", databaseName);

        var exists = await command.ExecuteScalarAsync(cancellationToken) is not null;

        if (exists)
        {
            return;
        }

        command.CommandText = string.Format(CreateDatabaseSql, databaseName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
