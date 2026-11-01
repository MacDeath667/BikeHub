using BikeHub.Mail.Transports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace BikeHub.Mail.Tests;

public sealed class MailFactory(string connectionString) : WebApplicationFactory<MailProgram>
{
    public const string ApiKey = "test-mail-service-key-at-least-32-characters";
    private Dictionary<string, string?> Settings => new()
    {
        ["ConnectionStrings:Mail"] = connectionString,
        ["Mail:Provider"] = "Development",
        ["Mail:ApiKey"] = ApiKey,
        ["Kafka:Enabled"] = "false",
        ["Database:MigrationsEnabled"] = "false"
    };

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(Settings));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings));
        builder.ConfigureTestServices(services =>
        {
            // Tests explicitly step the worker; HTTP and Kafka still use the real PostgreSQL queue.
            services.RemoveAll<IHostedService>();
        });
    }

    public HttpClient Client(bool authenticated = true)
    {
        var client = CreateClient();
        if (authenticated)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        }
        return client;
    }
}
