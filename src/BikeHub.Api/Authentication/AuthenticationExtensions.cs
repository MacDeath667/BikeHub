using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace BikeHub.Api.Authentication;

public static class AuthenticationExtensions
{
    public const string Scheme = "BikeHub";
    public static WebApplicationBuilder AddBikeHubAuthentication(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection("Authentication");
        var settings = section.Get<AuthOptions>() ?? new();
        var development = builder.Environment.IsDevelopment();
        if (settings.Enabled)
        {
            if (settings.CodeMinutes is < 1 or > 15 || settings.MaxAttempts is < 1 or > 10 ||
                settings.ResendSeconds is < 1 or > 3600 || settings.SessionDays is < 1 or > 30 ||
                settings.MaxPerEmailHour is < 1 or > 20 || settings.MaxPerDay is < 1 or > 10_000)
                throw new InvalidOperationException("Authentication limits are invalid.");
            if (settings.Store is not ("Memory" or "Postgres") || settings.EmailProvider is not ("Development" or "Smtp" or "GmailApi"))
                throw new InvalidOperationException("Select a supported authentication store and email provider.");
            if (!development && (settings.Store != "Postgres" || settings.EmailProvider == "Development" ||
                                 settings.OtpSecret.Length < 32 || !Path.IsPathFullyQualified(settings.DataProtectionPath)))
                throw new InvalidOperationException("Production authentication requires PostgreSQL, a real email transport, a 32-character or longer OTP secret and an absolute persistent Data Protection path.");
            if (settings.Store == "Postgres" && string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("BikeHub")))
                throw new InvalidOperationException("Configure ConnectionStrings:BikeHub for authentication storage.");
            if (settings.EmailProvider == "Smtp" && (string.IsNullOrWhiteSpace(settings.Smtp.Host) ||
                settings.Smtp.Port is < 1 or > 65535 || settings.Smtp.Security is not ("StartTls" or "SslOnConnect") ||
                string.IsNullOrWhiteSpace(settings.Smtp.Username) || string.IsNullOrWhiteSpace(settings.Smtp.Password) ||
                OtpSecurity.NormalizeEmail(settings.Smtp.FromAddress) is null))
                throw new InvalidOperationException("Configure a valid SMTP host, port, TLS mode, username, app password and sender address.");
            if (settings.EmailProvider == "GmailApi" && (string.IsNullOrWhiteSpace(settings.GmailApi.ClientId) ||
                !settings.GmailApi.ClientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(settings.GmailApi.ClientSecret) || string.IsNullOrWhiteSpace(settings.GmailApi.RefreshToken) ||
                OtpSecurity.NormalizeEmail(settings.GmailApi.FromAddress) is null))
                throw new InvalidOperationException("Configure a Google OAuth client, refresh token and sender address for Gmail API.");
        }
        builder.Services.Configure<AuthOptions>(section);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<OtpSecurity>();
        builder.Services.AddSingleton<DevelopmentMessages>();
        builder.Services.AddSingleton<IAuthenticationStore>(services =>
            settings.Enabled && settings.Store == "Postgres"
                ? new PostgresAuthenticationStore(builder.Configuration.GetConnectionString("BikeHub")!)
                : new InMemoryAuthenticationStore());
        builder.Services.AddSingleton<Google.Apis.Http.IHttpClientFactory, Google.Apis.Http.HttpClientFactory>();
        builder.Services.AddSingleton<IVerificationEmailSender>(services => settings.EmailProvider switch
        {
            "Development" => ActivatorUtilities.CreateInstance<DevelopmentEmailSender>(services),
            "GmailApi" => ActivatorUtilities.CreateInstance<GmailApiVerificationEmailSender>(services),
            _ => ActivatorUtilities.CreateInstance<SmtpVerificationEmailSender>(services)
        });
        builder.Services.AddSingleton<ISmsSender, TestSmsSender>();
        builder.Services.AddScoped<EmailAuthenticationService>();
        builder.Services.AddScoped<SessionCookieEvents>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddHostedService<AuthenticationCleanup>();

        var protection = builder.Services.AddDataProtection().SetApplicationName("BikeHub.Authentication");
        if (string.IsNullOrWhiteSpace(settings.DataProtectionPath))
            protection.UseEphemeralDataProtectionProvider();
        else
        {
            var directory = new DirectoryInfo(settings.DataProtectionPath);
            if (!directory.Exists)
            {
                if (OperatingSystem.IsWindows()) directory.Create();
                else Directory.CreateDirectory(directory.FullName,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            protection.PersistKeysToFileSystem(directory);
        }

        var secure = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        builder.Services.AddAuthentication(Scheme).AddCookie(Scheme, options =>
        {
            options.Cookie.Name = development ? "BikeHub.Auth" : "__Host-BikeHub.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = secure;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.SlidingExpiration = false;
            options.ExpireTimeSpan = TimeSpan.FromDays(settings.SessionDays);
            options.EventsType = typeof(SessionCookieEvents);
        });
        builder.Services.AddAuthorization(options =>
            options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(Scheme)
                .RequireAuthenticatedUser().RequireClaim("email_verified", "true").Build());
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = development || !settings.Enabled ? "BikeHub.Csrf" : "__Host-BikeHub.Csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.Path = "/";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = settings.Enabled ? secure : CookieSecurePolicy.SameAsRequest;
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("auth-request", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("auth-verify", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        builder.Logging.AddFilter("Microsoft.AspNetCore.Mvc.ModelBinding", LogLevel.Warning);
        return builder;
    }
}

public sealed class AuthenticationCleanup(IAuthenticationStore store, TimeProvider clock,
    IOptions<AuthOptions> options, ILogger<AuthenticationCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await store.CleanupAsync(clock.GetUtcNow(), stoppingToken); }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning("Authentication cleanup failed ({FailureType})", error.GetType().Name); }
        }
    }
}
