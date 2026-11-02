using Microsoft.EntityFrameworkCore.Migrations;

namespace BikeHub.Dal.Migrations;

public partial class UseSchemaLocalUuid : Migration
{
    private const string DefaultSql = "ALTER TABLE bikes ALTER COLUMN id SET DEFAULT uuidv7()";
    private const string ForwardOnlyMessage = "Schema isolation migrations cannot be rolled back automatically.";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DefaultSql);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException(ForwardOnlyMessage);
}
