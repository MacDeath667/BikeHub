using System.Text.Json;
using Confluent.Kafka;
using BikeHub.Mail.Contracts;
using Microsoft.Extensions.Options;

namespace BikeHub.Api.Authentication;

public sealed class VerificationKafkaOptions
{
    public string BootstrapServers { get; set; } = string.Empty;
    public string Topic { get; set; } = "bikehub.mail.send.v1";
    public SecurityProtocol SecurityProtocol { get; set; } = SecurityProtocol.Plaintext;
    public SaslMechanism SaslMechanism { get; set; } = SaslMechanism.Plain;
    public string? SaslUsername { get; set; }
    public string? SaslPassword { get; set; }
    public string? SslCaLocation { get; set; }
}

public interface IVerificationEventPublisher
{
    Task PublishAsync(SendEmailRequest message, CancellationToken cancellationToken);
}

public sealed class KafkaVerificationPublisher(IOptions<VerificationKafkaOptions> configuration) : IVerificationEventPublisher, IDisposable
{
    private readonly Lazy<IProducer<string, string>> producer = new(() => new ProducerBuilder<string, string>(new ProducerConfig
    {
        BootstrapServers = configuration.Value.BootstrapServers,
        EnableIdempotence = true,
        Acks = Acks.All,
        MessageTimeoutMs = 5000,
        AllowAutoCreateTopics = false,
        SecurityProtocol = configuration.Value.SecurityProtocol,
        SaslMechanism = configuration.Value.SaslMechanism,
        SaslUsername = configuration.Value.SaslUsername,
        SaslPassword = configuration.Value.SaslPassword,
        SslCaLocation = configuration.Value.SslCaLocation,
        ClientId = Constants.ApplicationName
    }).SetLogHandler((_, _) => { }).SetErrorHandler((_, _) => { }).Build());

    public async Task PublishAsync(SendEmailRequest message, CancellationToken cancellationToken)
    {
        await producer.Value.ProduceAsync(configuration.Value.Topic, new Message<string, string>
        { Key = message.MessageId.ToString(), Value = JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web)) }, cancellationToken);
    }

    public void Dispose()
    {
        if (producer.IsValueCreated)
        {
            producer.Value.Dispose();
        }
    }
}

// Persist before publishing. Broker outages and lost acknowledgements can replay the same messageId safely.
public sealed class VerificationOutbox(IAuthenticationStore store, VerificationPayload payload,
    IVerificationEventPublisher publisher, TimeProvider clock, IOptions<AuthOptions> options,
    ILogger<VerificationOutbox> logger) : BackgroundService
{
    private const string PublicationFailedLog = "Verification publication pending ({FailureType})";
    public async Task DispatchAsync(CancellationToken cancellationToken)
    {
        foreach (var challenge in await store.PendingPublicationsAsync(clock.GetUtcNow(), cancellationToken))
        {
            try
            {
                await publisher.PublishAsync(VerificationEmail.Create(payload.Read(challenge)), cancellationToken);
                await store.MarkPublishedAsync(challenge.Id, cancellationToken);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(PublicationFailedLog, error.GetType().Name);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled || options.Value.EmailProvider != Constants.EmailProviders.MailService)
        {
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DispatchAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(PublicationFailedLog, error.GetType().Name);
            }
        }
    }
}
