using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BikeHub.Dal.Migrations;

public partial class CommentReplies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE comments ADD COLUMN IF NOT EXISTS reply_to_id uuid NULL;
            ALTER TABLE group_replies ADD COLUMN IF NOT EXISTS reply_to_id uuid NULL;
            CREATE INDEX IF NOT EXISTS ix_comments_reply_to ON comments(reply_to_id);
            CREATE INDEX IF NOT EXISTS ix_group_replies_reply_to ON group_replies(reply_to_id);
            DO $migration$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_comment_reply_to' AND conrelid = 'comments'::regclass) THEN
                    ALTER TABLE comments ADD CONSTRAINT fk_comment_reply_to FOREIGN KEY (reply_to_id) REFERENCES comments(id) ON DELETE SET NULL;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_reply_reply_to' AND conrelid = 'group_replies'::regclass) THEN
                    ALTER TABLE group_replies ADD CONSTRAINT fk_reply_reply_to FOREIGN KEY (reply_to_id) REFERENCES group_replies(id) ON DELETE SET NULL;
                END IF;
            END $migration$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("This migration is forward-only. Restore a database backup to roll back.");
    }
}
