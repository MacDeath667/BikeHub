using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace BikeHub.Dal.Migrations;
public partial class RegistrationNicknames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE auth_email_challenges ADD COLUMN IF NOT EXISTS nickname varchar(32);
        UPDATE auth_accounts SET handle = NULL WHERE lower(handle) = replace(id::text, '-', '');
        UPDATE auth_accounts SET handle = lower(handle) WHERE handle IS NOT NULL AND handle <> lower(handle);
        CREATE UNIQUE INDEX IF NOT EXISTS ix_auth_accounts_nickname_lower ON auth_accounts (lower(handle)) WHERE handle IS NOT NULL;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("This migration is forward-only. Restore a database backup to roll back.");
}
