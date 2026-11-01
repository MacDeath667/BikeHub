using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BikeHub.Mail.Kafka;
using BikeHub.Mail.Persistence;
using BikeHub.Mail.Transports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace BikeHub.Mail;

public partial class MailProgram
{
    private const string ConfigurationError = "Configure ConnectionStrings:Mail and valid Mail, SMTP/Gmail and Kafka settings.";
    private const string GoogleClientIdSuffix = ".apps.googleusercontent.com";
    private const string ServerFailure = "Mail service is temporarily unavailable.";

    public static async Task Main(string[] args)
    {
        if (args.Contains("--check-transport", StringComparer.Ordinal))
        {
            var probeBuilder = WebApplication.CreateBuilder(args.Where(x => x is not ("--check-transport" or "--configuration-stdin")).ToArray());
            if (args.Contains("--configuration-stdin", StringComparer.Ordinal))
            {
                var input = await Console.In.ReadToEndAsync();
                probeBuilder.Configuration.AddInMemoryCollection(JsonSerializer.Deserialize<Dictionary<string, string?>>(input) ?? []);
            }
            var result = await TransportProbe.CheckAsync(probeBuilder.Configuration, CancellationToken.None, probeBuilder.Environment.IsDevelopment());
            Console.WriteLine(JsonSerializer.Serialize(result));
            Environment.ExitCode = result.Ok ? 0 : 1;
            return;
        }
        var migrateOnly = args.Contains(Constants.MigrateOnlyArgument, StringComparer.Ordinal);
        var builder = WebApplication.CreateBuilder(args.Where(x => x != Constants.MigrateOnlyArgument).ToArray());
        builder.Services.AddDbContextFactory<MailDbContext>(options => options.UseNpgsql(
            builder.Configuration.GetConnectionString(Constants.ConnectionStringName),
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", Constants.Schema)));
        if (!migrateOnly)
        {
            ValidateConfiguration(builder.Configuration, builder.Environment.IsDevelopment());
            builder.AddBikeHubObservability();
            builder.Services.Configure<MailOptions>(builder.Configuration.GetSection("Mail"));
            builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection("Kafka"));
            builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
            builder.Services.Configure<GmailApiOptions>(builder.Configuration.GetSection("GmailApi"));
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<MailQueue>();
            builder.Services.AddSingleton<KafkaIngress>();
            builder.Services.AddSingleton<Google.Apis.Http.IHttpClientFactory, Google.Apis.Http.HttpClientFactory>();
            builder.Services.AddSingleton<IEmailTransport>(services =>
                services.GetRequiredService<IOptions<MailOptions>>().Value.Provider switch
                {
                    Constants.GmailProvider => ActivatorUtilities.CreateInstance<GmailApiEmailTransport>(services),
                    Constants.SmtpProvider => ActivatorUtilities.CreateInstance<SmtpEmailTransport>(services),
                    _ => ActivatorUtilities.CreateInstance<DevelopmentEmailTransport>(services)
                });
            builder.Services.AddHostedService<DeliveryWorker>();
            builder.Services.AddHostedService<KafkaMailConsumer>();
            builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters
                .Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
        }
        var app = builder.Build();
        if (migrateOnly || builder.Configuration.GetValue("Database:MigrationsEnabled", false))
        {
            await using var context = await app.Services.GetRequiredService<IDbContextFactory<MailDbContext>>().CreateDbContextAsync();
            if (builder.Configuration.GetValue("Database:BootstrapEnabled", false))
            {
                var creator = context.GetService<IRelationalDatabaseCreator>();
                if (!await creator.ExistsAsync())
                {
                    await creator.CreateAsync();
                }
            }
            await context.Database.MigrateAsync();
        }
        if (migrateOnly)
        {
            await app.DisposeAsync();
            return;
        }
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                var expected = app.Services.GetRequiredService<IOptions<MailOptions>>().Value.ApiKey;
                var actual = context.Request.Headers[Constants.ApiKeyHeader].ToString();
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
                        SHA256.HashData(Encoding.UTF8.GetBytes(actual))))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
            }
            try { await next(context); }
            catch (Exception error) when (!context.RequestAborted.IsCancellationRequested)
            {
                app.Logger.LogError("Mail HTTP request failed ({FailureType})", error.GetType().Name);
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new { error = ServerFailure });
            }
        });
        app.MapGet("/health/live", () => Results.Ok());
        app.MapGet("/health/ready", async (IDbContextFactory<MailDbContext> factory, CancellationToken cancellationToken) =>
        {
            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            return await context.Database.CanConnectAsync(cancellationToken) &&
                !(await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any()
                ? Results.Ok() : Results.StatusCode(503);
        });
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.MapControllers();
        await app.RunAsync();
    }

    public static void ValidateConfiguration(IConfiguration configuration, bool development)
    {
        var mail = configuration.GetSection("Mail").Get<MailOptions>() ?? new();
        var smtp = configuration.GetSection("Smtp").Get<SmtpOptions>() ?? new();
        var gmail = configuration.GetSection("GmailApi").Get<GmailApiOptions>() ?? new();
        var kafka = configuration.GetSection("Kafka").Get<KafkaOptions>() ?? new();
        var anonymousSmtp = SmtpEmailTransport.AnonymousDevelopment(smtp, development);
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(Constants.ConnectionStringName)) ||
            mail.ApiKey.Length < 32 || mail.MaxAttempts is < 1 or > 10 || mail.RetrySeconds is < 1 or > 3600 ||
            mail.Provider is not (Constants.SmtpProvider or Constants.GmailProvider or Constants.DevelopmentProvider) ||
            (!development && mail.Provider == Constants.DevelopmentProvider) ||
            (mail.Provider == Constants.SmtpProvider && (string.IsNullOrWhiteSpace(smtp.Host) || smtp.Port is < 1 or > 65535 ||
                (!anonymousSmtp && (smtp.Security is not (Constants.SmtpSecurity.StartTls or Constants.SmtpSecurity.SslOnConnect) ||
                    string.IsNullOrWhiteSpace(smtp.Username) || string.IsNullOrWhiteSpace(smtp.Password))) || !ValidAddress(smtp.FromAddress))) ||
            (mail.Provider == Constants.GmailProvider && (!gmail.ClientId.EndsWith(GoogleClientIdSuffix, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(gmail.ClientSecret) || string.IsNullOrWhiteSpace(gmail.RefreshToken) || !ValidAddress(gmail.FromAddress))) ||
            (kafka.Enabled && (string.IsNullOrWhiteSpace(kafka.BootstrapServers) || string.IsNullOrWhiteSpace(kafka.Topic) ||
                string.IsNullOrWhiteSpace(kafka.GroupId))))
        {
            throw new InvalidOperationException(ConfigurationError);
        }
    }

    private static bool ValidAddress(string value) => System.Net.Mail.MailAddress.TryCreate(value, out var address) && address.Address == value;
}
