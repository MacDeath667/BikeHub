using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BikeHub.Mail.Persistence;

public static class MailDatabase
{
    public const string ConfigurationKey = "Database:Schema";
    private const string DefaultHistoryTable = "__EFMigrationsHistory";
    private const string SharedSchemaHistoryTable = "__MailMigrationsHistory";
    private const string InvalidSchema = "Database:Schema must be a single lowercase PostgreSQL identifier (maximum 63 characters).";

    public static void Configure(DbContextOptionsBuilder options, string? connectionString, string? schema = null)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString ?? string.Empty);
        schema ??= Constants.Schema;
        Validate(schema);
        connection.SearchPath = schema;
        // Preserve the deployed mail migration journal; use a distinct name when sharing an API schema.
        var history = schema == Constants.Schema ? DefaultHistoryTable : SharedSchemaHistoryTable;
        options.UseNpgsql(connection.ConnectionString, postgres => postgres.MigrationsHistoryTable(history, schema));
    }

    public static async Task MigrateAsync(MailDbContext context, CancellationToken cancellationToken = default)
    {
        var schema = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString()).SearchPath;
        Validate(schema);
        // Only a validated identifier is interpolated; parameters cannot represent a schema name.
#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schema}\"", cancellationToken);
#pragma warning restore EF1002
        await context.Database.MigrateAsync(cancellationToken);
    }

    private static void Validate(string? schema)
    {
        if (schema is null || !Regex.IsMatch(schema, "^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(InvalidSchema);
        }
    }
}
