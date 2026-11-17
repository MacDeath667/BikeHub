using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BikeHub.Dal.Migrations;

public partial class PrivateMessages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS private_conversations (
                id uuid CONSTRAINT pk_private_conversations PRIMARY KEY,
                first_user_id uuid NULL CONSTRAINT fk_private_conversation_first REFERENCES auth_accounts(id) ON DELETE SET NULL,
                second_user_id uuid NULL CONSTRAINT fk_private_conversation_second REFERENCES auth_accounts(id) ON DELETE SET NULL,
                requested_by_id uuid NULL CONSTRAINT fk_private_conversation_requester REFERENCES auth_accounts(id) ON DELETE SET NULL,
                status integer NOT NULL, latest_sequence bigint NOT NULL, first_read_sequence bigint NOT NULL, second_read_sequence bigint NOT NULL,
                first_archived boolean NOT NULL, second_archived boolean NOT NULL, created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL,
                CONSTRAINT ck_private_conversation_users CHECK (first_user_id IS NULL OR second_user_id IS NULL OR first_user_id <> second_user_id),
                CONSTRAINT ck_private_conversation_values CHECK (status IN (1, 2, 3) AND latest_sequence >= 0 AND
                    first_read_sequence BETWEEN 0 AND latest_sequence AND second_read_sequence BETWEEN 0 AND latest_sequence)
            );
            CREATE TABLE IF NOT EXISTS private_messages (
                id uuid CONSTRAINT pk_private_messages PRIMARY KEY,
                conversation_id uuid NOT NULL CONSTRAINT fk_private_message_conversation REFERENCES private_conversations(id) ON DELETE CASCADE,
                sender_id uuid NULL CONSTRAINT fk_private_message_sender REFERENCES auth_accounts(id) ON DELETE SET NULL,
                sequence bigint NOT NULL, client_message_id uuid NOT NULL, request_hash bytea NOT NULL,
                text varchar(4000) NOT NULL, is_deleted boolean NOT NULL, is_hidden boolean NOT NULL, created_at timestamptz NOT NULL,
                CONSTRAINT ck_private_message_values CHECK (sequence > 0 AND octet_length(request_hash) = 32)
            );
            CREATE TABLE IF NOT EXISTS message_blocks (
                user_id uuid NOT NULL CONSTRAINT fk_message_block_user REFERENCES auth_accounts(id) ON DELETE CASCADE,
                blocked_user_id uuid NOT NULL CONSTRAINT fk_message_block_target REFERENCES auth_accounts(id) ON DELETE CASCADE,
                created_at timestamptz NOT NULL, CONSTRAINT pk_message_blocks PRIMARY KEY (user_id, blocked_user_id),
                CONSTRAINT ck_message_block_users CHECK (user_id <> blocked_user_id)
            );
            CREATE TABLE IF NOT EXISTS private_message_reports (
                id uuid CONSTRAINT pk_private_message_reports PRIMARY KEY,
                reporter_id uuid NOT NULL CONSTRAINT fk_private_message_report_reporter REFERENCES auth_accounts(id) ON DELETE CASCADE,
                reported_user_id uuid NULL CONSTRAINT fk_private_message_report_target REFERENCES auth_accounts(id) ON DELETE SET NULL,
                conversation_id uuid NOT NULL CONSTRAINT fk_private_message_report_conversation REFERENCES private_conversations(id) ON DELETE CASCADE,
                message_id uuid NULL CONSTRAINT fk_private_message_report_message REFERENCES private_messages(id) ON DELETE SET NULL,
                message_text varchar(4000) NOT NULL, reason varchar(2000) NOT NULL, created_at timestamptz NOT NULL,
                resolved_at timestamptz NULL, resolved_by_id uuid NULL CONSTRAINT fk_private_message_report_moderator REFERENCES auth_accounts(id) ON DELETE SET NULL,
                resolution varchar(2000) NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_private_conversations_pair ON private_conversations(first_user_id, second_user_id);
            CREATE INDEX IF NOT EXISTS ix_private_conversations_first ON private_conversations(first_user_id, updated_at, id);
            CREATE INDEX IF NOT EXISTS ix_private_conversations_second ON private_conversations(second_user_id, updated_at, id);
            CREATE INDEX IF NOT EXISTS ix_private_conversations_requests ON private_conversations(requested_by_id, created_at);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_private_messages_sequence ON private_messages(conversation_id, sequence);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_private_messages_idempotency ON private_messages(sender_id, client_message_id);
            CREATE INDEX IF NOT EXISTS ix_private_messages_rate ON private_messages(sender_id, created_at);
            CREATE INDEX IF NOT EXISTS ix_message_blocks_target ON message_blocks(blocked_user_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_private_message_reports_open ON private_message_reports(reporter_id, message_id) WHERE resolved_at IS NULL;
            CREATE INDEX IF NOT EXISTS ix_private_message_reports_queue ON private_message_reports(resolved_at, created_at, id);
            CREATE INDEX IF NOT EXISTS ix_private_message_reports_target ON private_message_reports(reported_user_id);
            CREATE INDEX IF NOT EXISTS ix_private_message_reports_conversation ON private_message_reports(conversation_id);
            CREATE INDEX IF NOT EXISTS ix_private_message_reports_message ON private_message_reports(message_id);
            CREATE INDEX IF NOT EXISTS ix_private_message_reports_moderator ON private_message_reports(resolved_by_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("This migration is forward-only. Restore a database backup to roll back.");
    }
}
