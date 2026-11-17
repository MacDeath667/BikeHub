using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Dal;

internal static class MessageModel
{
    internal static void Configure(ModelBuilder model)
    {
        var conversation = model.Entity<PrivateConversation>();
        conversation.ToTable("private_conversations", t =>
        {
            t.HasCheckConstraint("ck_private_conversation_users", "first_user_id IS NULL OR second_user_id IS NULL OR first_user_id <> second_user_id");
            t.HasCheckConstraint("ck_private_conversation_values", "status IN (1, 2, 3) AND latest_sequence >= 0 AND first_read_sequence BETWEEN 0 AND latest_sequence AND second_read_sequence BETWEEN 0 AND latest_sequence");
        });
        conversation.HasKey(x => x.Id).HasName("pk_private_conversations");
        conversation.Property(x => x.Id).ValueGeneratedNever();
        conversation.HasIndex(x => new { x.FirstUserId, x.SecondUserId }).IsUnique().HasDatabaseName("ix_private_conversations_pair");
        conversation.HasIndex(x => new { x.FirstUserId, x.UpdatedAt, x.Id }).HasDatabaseName("ix_private_conversations_first");
        conversation.HasIndex(x => new { x.SecondUserId, x.UpdatedAt, x.Id }).HasDatabaseName("ix_private_conversations_second");
        conversation.HasIndex(x => new { x.RequestedById, x.CreatedAt }).HasDatabaseName("ix_private_conversations_requests");
        conversation.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.FirstUserId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_private_conversation_first");
        conversation.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.SecondUserId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_private_conversation_second");
        conversation.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.RequestedById).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_private_conversation_requester");
        var message = model.Entity<PrivateMessage>();
        message.ToTable("private_messages", t => t.HasCheckConstraint("ck_private_message_values", "sequence > 0 AND octet_length(request_hash) = 32"));
        message.HasKey(x => x.Id).HasName("pk_private_messages");
        message.Property(x => x.Id).ValueGeneratedNever();
        message.Property(x => x.Text).HasMaxLength(4000);
        message.Property(x => x.RequestHash).HasColumnType("bytea");
        message.HasIndex(x => new { x.ConversationId, x.Sequence }).IsUnique().HasDatabaseName("ix_private_messages_sequence");
        message.HasIndex(x => new { x.SenderId, x.ClientMessageId }).IsUnique().HasDatabaseName("ix_private_messages_idempotency");
        message.HasIndex(x => new { x.SenderId, x.CreatedAt }).HasDatabaseName("ix_private_messages_rate");
        message.HasOne<PrivateConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_private_message_conversation");
        message.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_private_message_sender");
        var block = model.Entity<MessageBlock>();
        block.ToTable("message_blocks", t => t.HasCheckConstraint("ck_message_block_users", "user_id <> blocked_user_id"));
        block.HasKey(x => new { x.UserId, x.BlockedUserId }).HasName("pk_message_blocks");
        block.HasIndex(x => x.BlockedUserId).HasDatabaseName("ix_message_blocks_target");
        block.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_message_block_user");
        block.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.BlockedUserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_message_block_target");
        var report = model.Entity<PrivateMessageReport>();
        report.ToTable("private_message_reports");
        report.HasKey(x => x.Id).HasName("pk_private_message_reports");
        report.Property(x => x.Id).ValueGeneratedNever();
        report.Property(x => x.MessageText).HasMaxLength(4000);
        report.Property(x => x.Reason).HasMaxLength(2000);
        report.Property(x => x.Resolution).HasMaxLength(2000);
        report.HasIndex(x => new { x.ReporterId, x.MessageId }).IsUnique().HasDatabaseName("ix_private_message_reports_open").HasFilter("resolved_at IS NULL");
        report.HasIndex(x => new { x.ResolvedAt, x.CreatedAt, x.Id }).HasDatabaseName("ix_private_message_reports_queue");
        report.HasIndex(x => x.ReportedUserId).HasDatabaseName("ix_private_message_reports_target");
        report.HasIndex(x => x.ConversationId).HasDatabaseName("ix_private_message_reports_conversation");
        report.HasIndex(x => x.MessageId).HasDatabaseName("ix_private_message_reports_message");
        report.HasIndex(x => x.ResolvedById).HasDatabaseName("ix_private_message_reports_moderator");
        report.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.ReporterId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_private_message_report_reporter");
        report.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.ReportedUserId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_private_message_report_target");
        report.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.ResolvedById).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_private_message_report_moderator");
        report.HasOne<PrivateConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_private_message_report_conversation");
        report.HasOne<PrivateMessage>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_private_message_report_message");
    }
}
