using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace BikeHub.Dal.Migrations;

public partial class PostRoutes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE posts ADD COLUMN IF NOT EXISTS route_json jsonb;
        ALTER TABLE group_entries ADD COLUMN IF NOT EXISTS route_json jsonb;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("This migration is forward-only. Restore a database backup to roll back.");
}
