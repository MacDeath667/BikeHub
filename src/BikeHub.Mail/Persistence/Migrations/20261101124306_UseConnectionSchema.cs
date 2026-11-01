using Microsoft.EntityFrameworkCore.Migrations;

namespace BikeHub.Mail.Persistence.Migrations;

public partial class UseConnectionSchema : Migration
{
    private const string ForwardOnlyMessage = "Schema isolation migrations cannot be rolled back automatically.";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Mapping only: existing tables stay in mail; new instances use their connection search_path.
        // Moving tables here would move production data when deploying a separate staging instance.
    }

    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException(ForwardOnlyMessage);
}
