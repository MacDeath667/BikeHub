using Microsoft.EntityFrameworkCore.Migrations;

namespace BikeHub.Dal.Migrations;

public partial class SeparateLoginAndRegistration : Migration
{
    private const string PurposeSql = "ALTER TABLE auth_email_challenges ADD COLUMN IF NOT EXISTS purpose integer NOT NULL DEFAULT 0";
    private const string ForwardOnlyMessage = "Removing the challenge purpose would break login and registration isolation.";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(PurposeSql);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException(ForwardOnlyMessage);
}
