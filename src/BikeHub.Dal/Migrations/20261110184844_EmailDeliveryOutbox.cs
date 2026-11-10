using Microsoft.EntityFrameworkCore.Migrations;

namespace BikeHub.Dal.Migrations;

public partial class EmailDeliveryOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE auth_email_challenges ADD COLUMN IF NOT EXISTS event_published boolean NOT NULL DEFAULT false;
            ALTER TABLE auth_email_challenges ADD COLUMN IF NOT EXISTS last_resend_at timestamp with time zone;
            ALTER TABLE auth_email_challenges ADD COLUMN IF NOT EXISTS protected_message text;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Authentication migrations are forward-only.");
    }
}
