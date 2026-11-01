using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace BikeHub.Mail.Kafka;

public sealed class KafkaMailConsumer(KafkaIngress ingress, IOptions<KafkaOptions> options,
    ILogger<KafkaMailConsumer> logger) : BackgroundService
{
    private const string BrokerFailureLog = "Kafka consumer error ({ErrorCode})";
    private const string PersistFailureLog = "Kafka inbox persistence failed ({FailureType}); offset not committed";
    private const string CommitFailureLog = "Kafka offset commit failed ({ErrorCode}); replay is safe";

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(async () =>
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }
        using var consumer = new ConsumerBuilder<Ignore, string>(new ConsumerConfig
        {
            BootstrapServers = settings.BootstrapServers,
            GroupId = settings.GroupId,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            AllowAutoCreateTopics = false,
            SecurityProtocol = settings.SecurityProtocol,
            SaslMechanism = settings.SaslMechanism,
            SaslUsername = settings.SaslUsername,
            SaslPassword = settings.SaslPassword,
            SslCaLocation = settings.SslCaLocation,
            ClientId = Constants.ApplicationName
        }).SetErrorHandler((_, error) => logger.LogWarning(BrokerFailureLog, error.Code))
          .SetLogHandler((_, _) => { }).Build();
        consumer.Subscribe(settings.Topic);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<Ignore, string> record;
                try
                {
                    record = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException error)
                {
                    logger.LogWarning(BrokerFailureLog, error.Error.Code);
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }
                // Keep this record until its inbox row or rejection has been committed to PostgreSQL.
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await ingress.HandleAsync(record.Topic, record.Partition.Value, record.Offset.Value,
                            record.Message.Value, stoppingToken);
                        break;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception error)
                    {
                        logger.LogError(PersistFailureLog, error.GetType().Name);
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    }
                }
                stoppingToken.ThrowIfCancellationRequested();
                try { consumer.Commit(record); }
                catch (KafkaException error) { logger.LogWarning(CommitFailureLog, error.Error.Code); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            try { consumer.Close(); }
            catch (KafkaException error) { logger.LogWarning(BrokerFailureLog, error.Error.Code); }
        }
    }, CancellationToken.None);
}
