using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace BikeHub.Dal.Migrations;
public partial class ContentDrafts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE IF NOT EXISTS content_drafts (
            owner_id uuid NOT NULL CONSTRAINT fk_content_draft_owner REFERENCES auth_accounts(id) ON DELETE CASCADE,
            scope varchar(120) NOT NULL,
            revision bigint NOT NULL,
            state_json jsonb,
            image_ids uuid[] NOT NULL,
            updated_at timestamptz NOT NULL,
            CONSTRAINT pk_content_drafts PRIMARY KEY (owner_id, scope)
        );
        CREATE INDEX IF NOT EXISTS ix_content_drafts_expiry ON content_drafts(updated_at);
        """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("This migration is forward-only. Restore a database backup to roll back.");
}
