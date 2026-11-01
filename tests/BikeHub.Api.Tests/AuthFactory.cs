using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using BikeHub.Api.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace BikeHub.Api.Tests;

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan value) => now += value;
}

public sealed class LogCapture : ILoggerProvider
{
    public ConcurrentQueue<string> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(Entries);
    public void Dispose() { }
    private sealed class CaptureLogger(ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => entries.Enqueue(formatter(state, exception) + (exception is null ? "" : "\n" + exception));
    }
}

public sealed class AuthFactory : WebApplicationFactory<Program>
{
    public TestClock Clock { get; } = new();
    public LogCapture Logs { get; } = new();
    public string EnvironmentName { get; init; } = "Development";
    public Dictionary<string, string?> Settings { get; } = new()
    {
        ["Observability:Enabled"] = "false",
        ["Database:BootstrapEnabled"] = "false",
        ["Database:MigrationsEnabled"] = "false",
        ["Authentication:Enabled"] = "true",
        ["Authentication:Store"] = "Memory",
        ["Authentication:EmailProvider"] = "Development"
    };
    public IVerificationEmailSender? Sender { get; init; }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(Settings));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<ILoggerProvider>(Logs);
            services.AddSingleton<IStartupFilter, LocalConnection>();
            if (Sender is not null)
            {
                services.RemoveAll<IVerificationEmailSender>();
                services.AddSingleton(Sender);
            }
        });
    }

    public HttpClient Browser(bool https = false) => CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri(https ? "https://localhost" : "http://localhost"), AllowAutoRedirect = false, HandleCookies = true });

    private sealed class LocalConnection : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                context.Connection.RemoteIpAddress = context.Request.Headers.ContainsKey("X-Test-Remote")
                    ? IPAddress.Parse("192.0.2.1") : IPAddress.Loopback;
                await following();
            });
            next(app);
        };
    }
}

public static class AuthTestClient
{
    public static async Task<HttpResponseMessage> Post(HttpClient client, string path, object? body = null, bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        { Content = JsonContent.Create(body ?? new { }) };
        if (csrf)
        {
            var bootstrap = await client.GetFromJsonAsync<AuthBootstrap>("/api/auth/session");
            request.Headers.Add("X-CSRF-TOKEN", bootstrap!.CsrfToken);
        }
        return await client.SendAsync(request);
    }

    public static async Task<(EmailCodeResponse Challenge, string Code)> Challenge(HttpClient client, string email)
    {
        using var response = await Post(client, "/api/auth/email/challenges", new { email, name = "Участник теста" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var challenge = (await response.Content.ReadFromJsonAsync<EmailCodeResponse>())!;
        var message = await client.GetFromJsonAsync<DevelopmentEmail>("/api/auth/development/messages/" + challenge.ChallengeId);
        return (challenge, message!.Code);
    }

    public static async Task<(AuthBootstrap Session, HttpResponseMessage Response)> Login(HttpClient client, string email)
    {
        var (challenge, code) = await Challenge(client, email);
        var response = await Post(client, "/api/auth/email/verify", new { challengeId = challenge.ChallengeId, code });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<AuthBootstrap>())!, response);
    }

    private sealed record DevelopmentEmail(string Code);
}
