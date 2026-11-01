using System.Security.Cryptography;
using Testcontainers.PostgreSql;
using Xunit;

namespace BikeHub.Testing;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18.6")
        .WithDatabase("bikehub_test")
        .WithUsername("bikehub_test")
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
        .Build();

    public static string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        ConnectionString = container.GetConnectionString();
    }

    public async Task DisposeAsync() => await container.DisposeAsync();
}
