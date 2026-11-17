using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BikeHub.Dal.Migrations;

[DbContext(typeof(BikeHubDbContext))]
[Migration("20261117120000_CanonicalCities")]
public sealed class CanonicalCities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Pin both the data and keys: future catalog/normalizer changes cannot alter this migration.
        using var stream = typeof(CanonicalCities).Assembly.GetManifestResourceStream("BikeHub.Dal.ReferenceData.CanonicalCityAliases.v1.json")!;
        using var reader = new StreamReader(stream);
        var aliases = reader.ReadToEnd().Replace("'", "''", StringComparison.Ordinal);
        foreach (var table in new[] { "auth_accounts", "rides", "library_routes" })
        {
            // Only known unique aliases change. Ambiguous and unknown legacy values remain untouched.
            migrationBuilder.Sql($"""
                WITH aliases AS (
                    SELECT * FROM jsonb_to_recordset('{aliases}'::jsonb) AS city(key text, label text)
                )
                UPDATE {table} AS target SET city = aliases.label FROM aliases
                WHERE regexp_replace(
                    regexp_replace(translate(lower(trim(target.city)), 'ё', 'е'), '^(город\s+|г\.\s*|г\s+)', ''),
                    '[^[:alnum:]]', '', 'g') = aliases.key AND target.city <> aliases.label;
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("City normalization is forward-only; restore a backup to recover original spellings.");
}
