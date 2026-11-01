using System.Text.Json;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Dal;

public sealed class BikeHubDbContext(DbContextOptions<BikeHubDbContext> options) : DbContext(options)
{
    public DbSet<Bike> Bikes => Set<Bike>();
    public DbSet<AuthAccountEntity> Accounts => Set<AuthAccountEntity>();
    public DbSet<EmailChallengeEntity> EmailChallenges => Set<EmailChallengeEntity>();
    public DbSet<AuthSessionEntity> Sessions => Set<AuthSessionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var bikes = modelBuilder.Entity<Bike>();
        bikes.ToTable("bikes");
        bikes.HasKey(x => x.Id).HasName("bikes_pkey");
        bikes.Property(x => x.Id).HasDefaultValueSql("public.uuidv7()");
        bikes.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        bikes.HasIndex(x => x.OwnerId).HasDatabaseName("ix_bikes_owner_id");

        var accounts = modelBuilder.Entity<AuthAccountEntity>();
        accounts.ToTable("auth_accounts", table =>
        {
            table.HasCheckConstraint("auth_accounts_email_check", "length(email) <= 254");
            table.HasCheckConstraint("auth_accounts_display_name_check", "length(display_name) BETWEEN 1 AND 60");
        });
        accounts.HasKey(x => x.Id).HasName("auth_accounts_pkey");
        accounts.Property(x => x.Id).ValueGeneratedNever();
        accounts.HasAlternateKey(x => x.Email).HasName("auth_accounts_email_key");
        accounts.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        accounts.HasIndex(x => x.PhoneE164).IsUnique().HasDatabaseName("ix_auth_accounts_phone")
            .HasFilter("phone_e164 IS NOT NULL");

        var challenges = modelBuilder.Entity<EmailChallengeEntity>();
        challenges.ToTable("auth_email_challenges", table =>
        {
            table.HasCheckConstraint("auth_email_challenges_email_check", "length(email) <= 254");
            table.HasCheckConstraint("auth_email_challenges_display_name_check", "length(display_name) BETWEEN 1 AND 60");
            table.HasCheckConstraint("auth_email_challenges_code_hash_check", "length(code_hash) = 64");
            table.HasCheckConstraint("auth_email_challenges_binding_hash_check", "length(binding_hash) = 64");
            table.HasCheckConstraint("auth_email_challenges_attempts_check", "attempts >= 0");
        });
        challenges.HasKey(x => x.Id).HasName("auth_email_challenges_pkey");
        challenges.Property(x => x.Id).ValueGeneratedNever();
        challenges.Property(x => x.Attempts).HasDefaultValue(0);
        challenges.Property(x => x.Delivered).HasDefaultValue(false);
        challenges.Property(x => x.Consumed).HasDefaultValue(false);
        challenges.HasIndex(x => new { x.Email, x.CreatedAt }).HasDatabaseName("ix_auth_email_challenges_email_created");
        challenges.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_auth_email_challenges_created");
        challenges.HasIndex(x => x.BindingHash).HasDatabaseName("ix_auth_email_challenges_binding")
            .HasFilter("NOT consumed");

        var sessions = modelBuilder.Entity<AuthSessionEntity>();
        sessions.ToTable("auth_sessions");
        sessions.HasKey(x => x.Id).HasName("auth_sessions_pkey");
        sessions.Property(x => x.Id).ValueGeneratedNever();
        sessions.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("auth_sessions_account_id_fkey");
        sessions.HasIndex(x => x.AccountId).HasDatabaseName("ix_auth_sessions_account");
        sessions.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_auth_sessions_expires");

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name));
            }
        }
    }
}
