using BikeHub.Mail.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BikeHub.Mail.Persistence;

public sealed class MailMessage
{
    public Guid Id { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public string? Destination { get; set; }
    public string? Subject { get; set; }
    public string? TextBody { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DeliveryStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset NotBefore { get; set; }
    public string? FailureReason { get; set; }

    public EmailStatus ToStatus() => new(Id, Status, Attempts, CreatedAt, UpdatedAt, FailureReason);
    public SendEmailRequest ToRequest() => new(Id, Destination!, Subject!, TextBody!, ExpiresAt);
}

public sealed class KafkaRejection
{
    public string Topic { get; set; } = string.Empty;
    public int Partition { get; set; }
    public long Offset { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MailDbContext(DbContextOptions<MailDbContext> options) : DbContext(options)
{
    public DbSet<MailMessage> Messages => Set<MailMessage>();
    public DbSet<KafkaRejection> KafkaRejections => Set<KafkaRejection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var messages = modelBuilder.Entity<MailMessage>();
        messages.ToTable("messages");
        messages.HasKey(x => x.Id);
        messages.Property(x => x.Id).ValueGeneratedNever();
        messages.Property(x => x.Fingerprint).HasMaxLength(64);
        messages.HasIndex(x => new { x.Status, x.NotBefore });
        var rejections = modelBuilder.Entity<KafkaRejection>();
        rejections.ToTable("kafka_rejections");
        rejections.HasKey(x => new { x.Topic, x.Partition, x.Offset });
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name));
            }
        }
    }
}

public sealed class MailDbContextFactory : IDesignTimeDbContextFactory<MailDbContext>
{
    private const string ConnectionStringVariable = "ConnectionStrings__Mail";
    private const string DesignTimeConnectionString = "Host=localhost;Database=bikehub_mail;Username=bikehub_mail";

    public MailDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MailDbContext>();
        MailDatabase.Configure(options, Environment.GetEnvironmentVariable(ConnectionStringVariable) ?? DesignTimeConnectionString,
            Environment.GetEnvironmentVariable("Database__Schema"));
        return new(options.Options);
    }
}
