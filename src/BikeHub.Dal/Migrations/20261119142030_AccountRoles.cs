using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BikeHub.Dal.Migrations
{
    /// <inheritdoc />
    public partial class AccountRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE auth_accounts ADD COLUMN IF NOT EXISTS role integer NOT NULL DEFAULT 0;
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conrelid = 'auth_accounts'::regclass AND conname = 'auth_accounts_role_check'
                    ) THEN
                        ALTER TABLE auth_accounts ADD CONSTRAINT auth_accounts_role_check CHECK (role IN (0, 1, 2));
                    END IF;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Account roles are forward-only; restore a backup to recover previous assignments.");
        }
    }
}
