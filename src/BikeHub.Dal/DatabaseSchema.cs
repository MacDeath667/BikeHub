using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BikeHub.Dal;

public static class DatabaseSchema
{
    public const string ConfigurationKey = "Database:Schema";
    public const string Default = "public";
    private const string HistoryTable = "__EFMigrationsHistory";
    private const string InvalidSchema = "Database:Schema must be a single lowercase PostgreSQL identifier (maximum 63 characters).";

    public static void Configure(DbContextOptionsBuilder options, string? connectionString, string schema = Default)
    {
        Validate(schema);
        var connection = new NpgsqlConnectionStringBuilder(connectionString ?? string.Empty) { SearchPath = schema };
        options.UseNpgsql(connection.ConnectionString, postgres => postgres.MigrationsHistoryTable(HistoryTable, schema));
    }

    public static async Task EnsureAsync(DbContext context, CancellationToken cancellationToken)
    {
        var schema = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString()).SearchPath;
        Validate(schema);
        // The identifier is validated above; SQL parameters cannot represent schema names.
#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schema}\"", cancellationToken);
#pragma warning restore EF1002
    }

    private static void Validate(string? schema)
    {
        if (schema is null || !Regex.IsMatch(schema, "^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(InvalidSchema);
        }
    }
}
