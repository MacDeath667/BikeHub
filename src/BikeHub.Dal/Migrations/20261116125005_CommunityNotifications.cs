using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace BikeHub.Dal.Migrations;
public partial class CommunityNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE IF NOT EXISTS notifications (
            id uuid NOT NULL CONSTRAINT pk_notifications PRIMARY KEY,
            recipient_id uuid NOT NULL CONSTRAINT fk_notification_recipient REFERENCES auth_accounts(id) ON DELETE CASCADE,
            actor_id uuid NOT NULL CONSTRAINT fk_notification_actor REFERENCES auth_accounts(id) ON DELETE CASCADE,
            kind integer NOT NULL,
            target_type integer NOT NULL,
            target_id uuid NOT NULL,
            container_id uuid,
            created_at timestamptz NOT NULL,
            read_at timestamptz
        );
        CREATE INDEX IF NOT EXISTS "IX_notifications_actor_id" ON notifications(actor_id);
        CREATE UNIQUE INDEX IF NOT EXISTS ix_notifications_event ON notifications(recipient_id, actor_id, kind, target_type, target_id);
        CREATE INDEX IF NOT EXISTS ix_notifications_inbox ON notifications(recipient_id, created_at, id);
        CREATE INDEX IF NOT EXISTS ix_notifications_unread ON notifications(recipient_id, read_at) WHERE read_at IS NULL;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("This migration is forward-only. Restore a database backup to roll back.");
}
