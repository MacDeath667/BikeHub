using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace BikeHub.Dal.Migrations;

public partial class CommunityGroupsAndErasure : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE communities ADD COLUMN IF NOT EXISTS owner_id uuid;
            ALTER TABLE communities ADD COLUMN IF NOT EXISTS image_id uuid;
            ALTER TABLE communities ADD COLUMN IF NOT EXISTS is_deleted boolean NOT NULL DEFAULT false;
            ALTER TABLE communities ADD COLUMN IF NOT EXISTS is_hidden boolean NOT NULL DEFAULT false;
            CREATE TABLE IF NOT EXISTS group_entries (
                id uuid CONSTRAINT pk_group_entries PRIMARY KEY,
                group_id uuid NOT NULL CONSTRAINT fk_entry_group REFERENCES communities(id) ON DELETE RESTRICT,
                author_id uuid NOT NULL CONSTRAINT fk_entry_author REFERENCES auth_accounts(id) ON DELETE RESTRICT,
                kind integer NOT NULL,
                title varchar(160) NOT NULL, content varchar(20000) NOT NULL,
                image_ids uuid[] NOT NULL DEFAULT ARRAY[]::uuid[],
                created_at timestamptz NOT NULL, updated_at timestamptz,
                is_deleted boolean NOT NULL DEFAULT false, is_hidden boolean NOT NULL DEFAULT false
            );
            CREATE TABLE IF NOT EXISTS group_replies (
                id uuid CONSTRAINT pk_group_replies PRIMARY KEY,
                entry_id uuid NOT NULL CONSTRAINT fk_reply_entry REFERENCES group_entries(id) ON DELETE RESTRICT,
                author_id uuid NOT NULL CONSTRAINT fk_reply_author REFERENCES auth_accounts(id) ON DELETE RESTRICT,
                text varchar(2000) NOT NULL, created_at timestamptz NOT NULL, updated_at timestamptz,
                is_deleted boolean NOT NULL DEFAULT false, is_hidden boolean NOT NULL DEFAULT false
            );
            CREATE TABLE IF NOT EXISTS media_deletions (id uuid CONSTRAINT pk_media_deletions PRIMARY KEY);
            CREATE INDEX IF NOT EXISTS "IX_communities_image_id" ON communities(image_id);
            CREATE INDEX IF NOT EXISTS "IX_communities_owner_id" ON communities(owner_id);
            CREATE INDEX IF NOT EXISTS "IX_group_entries_author_id" ON group_entries(author_id);
            CREATE INDEX IF NOT EXISTS ix_group_entries_feed ON group_entries(group_id, created_at, id);
            CREATE INDEX IF NOT EXISTS "IX_group_replies_author_id" ON group_replies(author_id);
            CREATE INDEX IF NOT EXISTS ix_group_replies_thread ON group_replies(entry_id, created_at, id);
            DO $migration$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_group_owner' AND conrelid = 'communities'::regclass) THEN
                    ALTER TABLE communities ADD CONSTRAINT fk_group_owner FOREIGN KEY (owner_id) REFERENCES auth_accounts(id) ON DELETE RESTRICT;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_group_image' AND conrelid = 'communities'::regclass) THEN
                    ALTER TABLE communities ADD CONSTRAINT fk_group_image FOREIGN KEY (image_id) REFERENCES media_assets(id) ON DELETE RESTRICT;
                END IF;
            END $migration$;
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("This migration is forward-only. Restore a database backup to roll back.");
}
