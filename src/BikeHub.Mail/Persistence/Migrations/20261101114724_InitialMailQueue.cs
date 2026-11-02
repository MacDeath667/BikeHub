using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BikeHub.Mail.Persistence.Migrations;

public partial class InitialMailQueue : Migration
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS messages (
            id uuid NOT NULL CONSTRAINT "PK_messages" PRIMARY KEY,
            fingerprint varchar(64) NOT NULL,
            destination text NULL, subject text NULL, text_body text NULL,
            expires_at timestamptz NULL, status integer NOT NULL, attempts integer NOT NULL,
            created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL,
            not_before timestamptz NOT NULL, failure_reason text NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_messages_status_not_before" ON messages (status, not_before);
        CREATE TABLE IF NOT EXISTS kafka_rejections (
            topic text NOT NULL, partition integer NOT NULL, "offset" bigint NOT NULL,
            reason text NOT NULL, created_at timestamptz NOT NULL,
            CONSTRAINT "PK_kafka_rejections" PRIMARY KEY (topic, partition, "offset")
        );
        """;
    private const string ForwardOnlyMessage = "The mail baseline cannot be rolled back automatically because it may adopt existing tables.";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(SchemaSql);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException(ForwardOnlyMessage);
}
