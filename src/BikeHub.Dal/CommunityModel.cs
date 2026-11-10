using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Dal;

internal static class CommunityModel
{
    internal static void Configure(ModelBuilder model)
    {
        var account = model.Entity<AuthAccountEntity>();
        account.Property(x => x.Handle).HasMaxLength(32);
        account.Property(x => x.City).HasDefaultValue(string.Empty).HasMaxLength(100);
        account.Property(x => x.Bio).HasDefaultValue(string.Empty).HasMaxLength(2000);
        account.Property(x => x.IsBlocked).HasDefaultValue(false);
        account.HasIndex(x => x.Handle).IsUnique().HasDatabaseName("ix_auth_accounts_handle").HasFilter("handle IS NOT NULL");
        account.HasOne<MediaAsset>().WithMany().HasForeignKey(x => x.AvatarId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_account_avatar");
        var bike = model.Entity<Bike>();
        bike.Property(x => x.ImageIds).HasColumnType("uuid[]").HasDefaultValueSql("ARRAY[]::uuid[]");
        bike.Property(x => x.Brand).HasDefaultValue(string.Empty).HasMaxLength(80);
        bike.Property(x => x.Category).HasDefaultValue(3);
        bike.Property(x => x.Year).HasDefaultValue(0);
        bike.Property(x => x.Mileage).HasDefaultValue(0m);
        bike.Property(x => x.IsDeleted).HasDefaultValue(false);
        bike.Property(x => x.IsHidden).HasDefaultValue(false);
        bike.HasOne<MediaAsset>().WithMany().HasForeignKey(x => x.ImageId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_bike_image");
        var post = model.Entity<CommunityPost>();
        post.ToTable("posts");
        post.HasKey(x => x.Id).HasName("pk_posts");
        post.Property(x => x.Id).ValueGeneratedNever();
        post.Property(x => x.Title).HasMaxLength(160);
        post.Property(x => x.Content).HasMaxLength(20000);
        post.Property(x => x.Category).HasMaxLength(20);
        post.Property(x => x.ImageIds).HasColumnType("uuid[]").HasDefaultValueSql("ARRAY[]::uuid[]");
        post.Property(x => x.Tags).HasColumnType("text[]").HasDefaultValueSql("ARRAY[]::text[]");
        post.HasIndex(x => new { x.CreatedAt, x.Id }).HasDatabaseName("ix_posts_feed");
        post.HasOne<Bike>().WithMany().HasForeignKey(x => x.BikeId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_post_bike");
        post.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_post_author");
        post.HasOne<MediaAsset>().WithMany().HasForeignKey(x => x.ImageId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_post_image");
        var comment = model.Entity<CommunityComment>();
        comment.ToTable("comments");
        comment.HasKey(x => x.Id).HasName("pk_comments");
        comment.Property(x => x.Id).ValueGeneratedNever();
        comment.Property(x => x.Text).HasMaxLength(2000);
        comment.HasIndex(x => new { x.PostId, x.CreatedAt }).HasDatabaseName("ix_comments_post");
        comment.HasOne<CommunityPost>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_comment_post");
        comment.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_comment_author");
        var group = model.Entity<CommunityGroup>();
        group.ToTable("communities");
        group.HasKey(x => x.Id).HasName("pk_communities");
        group.Property(x => x.Id).ValueGeneratedNever();
        group.Property(x => x.Name).HasMaxLength(100);
        group.Property(x => x.Description).HasMaxLength(2000);
        var relation = model.Entity<CommunityRelation>();
        relation.ToTable("community_relations");
        relation.HasKey(x => new { x.UserId, x.TargetId, x.Kind }).HasName("pk_community_relations");
        relation.HasIndex(x => new { x.TargetId, x.Kind }).HasDatabaseName("ix_relations_target");
        relation.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_relation_user");
        var media = model.Entity<MediaAsset>();
        media.ToTable("media_assets");
        media.HasKey(x => x.Id).HasName("pk_media_assets");
        media.Property(x => x.Id).ValueGeneratedNever();
        media.HasIndex(x => new { x.OwnerId, x.CreatedAt }).HasDatabaseName("ix_media_owner");
        media.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_media_owner");
        var report = model.Entity<ModerationReport>();
        report.ToTable("moderation_reports");
        report.HasKey(x => x.Id).HasName("pk_moderation_reports");
        report.Property(x => x.Id).ValueGeneratedNever();
        report.Property(x => x.Reason).HasMaxLength(2000);
        report.Property(x => x.Resolution).HasMaxLength(2000);
        report.HasIndex(x => new { x.ReporterId, x.TargetType, x.TargetId }).IsUnique().HasDatabaseName("ix_reports_open").HasFilter("NOT resolved");
        report.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.ReporterId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_report_reporter");
    }
}
