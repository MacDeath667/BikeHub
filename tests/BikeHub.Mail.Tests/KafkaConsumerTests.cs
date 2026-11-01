using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BikeHub.Mail.Contracts;
using BikeHub.Mail.Kafka;
using BikeHub.Mail.Persistence;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BikeHub.Mail.Tests;

[Collection("Mail persistence")]
public sealed class KafkaConsumerTests
{
    [Fact]
    public async Task Broker_replays_share_http_idempotency_and_offsets_wait_for_database_recovery()
    {
        await using var kafka = new Testcontainers.Kafka.KafkaBuilder("confluentinc/cp-kafka:7.9.2").Build();
        await kafka.StartAsync();
        var broker = kafka.GetBootstrapAddress();
        var topic = "bikehub-mail-test-" + Guid.NewGuid().ToString("N");
        var group = "bikehub-mail-test-" + Guid.NewGuid().ToString("N");
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = broker }).Build();
        await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        try
        {
            await using var database = await MailTestDatabase.CreateAsync();
            await using var app = new MailFactory(database.ConnectionString);
            using var http = app.Client();
            var message = new SendEmailRequest(Guid.NewGuid(), "kafka@example.test", "Subject", "private-code");
            Assert.Equal(HttpStatusCode.Accepted, (await http.PostAsJsonAsync("/api/emails", message)).StatusCode);
            var factory = new RecoverableFactory(database);
            var queue = new MailQueue(factory, TimeProvider.System);
            using var consumer = new KafkaMailConsumer(new KafkaIngress(queue), Options.Create(new KafkaOptions
            { Enabled = true, BootstrapServers = broker, Topic = topic, GroupId = group }), NullLogger<KafkaMailConsumer>.Instance);
            using var observer = new ConsumerBuilder<Ignore, string>(new ConsumerConfig
            { BootstrapServers = broker, GroupId = group, EnableAutoCommit = false }).Build();
            using var producer = new ProducerBuilder<Null, string>(new ProducerConfig { BootstrapServers = broker, EnableIdempotence = true }).Build();
            await consumer.StartAsync(default);
            try
            {
                await producer.ProduceAsync(topic, new() { Value = JsonSerializer.Serialize(message) });
                await WaitAsync(() => Task.FromResult(factory.Failures > 0));
                var partition = new TopicPartition(topic, 0);
                var before = observer.Committed([partition], TimeSpan.FromSeconds(5)).Single().Offset;
                Assert.True(before == Offset.Unset || before.Value == 0);
                factory.Available = true;
                await producer.ProduceAsync(topic, new() { Value = JsonSerializer.Serialize(message) });
                await producer.ProduceAsync(topic, new() { Value = "invalid private-code" });
                await producer.ProduceAsync(topic, new() { Value = JsonSerializer.Serialize(message with { TextBody = "changed" }) });
                await WaitAsync(() => Task.FromResult(observer.Committed([partition], TimeSpan.FromSeconds(5)).Single().Offset.Value == 4));
                await using var context = database.CreateDbContext();
                Assert.Single(await context.Messages.ToListAsync());
                Assert.Equal(2, await context.KafkaRejections.CountAsync());
                Assert.All(await context.KafkaRejections.Select(x => x.Reason).ToListAsync(), reason =>
                    Assert.Contains(reason, new[] { "invalid_payload", "idempotency_conflict" }));
            }
            finally
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await consumer.StopAsync(timeout.Token);
            }
        }
        finally
        {
            await admin.DeleteTopicsAsync([topic]);
        }
    }

    private static async Task WaitAsync(Func<Task<bool>> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate())
            {
                return;
            }
            await Task.Delay(200);
        }
        Assert.Fail("Kafka integration condition was not reached.");
    }

    private sealed class RecoverableFactory(IDbContextFactory<MailDbContext> inner) : IDbContextFactory<MailDbContext>
    {
        private int failures;
        public volatile bool Available;
        public int Failures => Volatile.Read(ref failures);
        public MailDbContext CreateDbContext()
        {
            if (!Available)
            {
                Interlocked.Increment(ref failures);
                throw new IOException("simulated database outage");
            }
            return inner.CreateDbContext();
        }
    }
}
