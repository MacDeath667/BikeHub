using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using BikeHub.Dal;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Authentication;

public static class AuthenticationExtensions
{
    private const string ConfigurationSection = "Authentication";
    private const string SmtpConfigurationSection = "Smtp";
    private const string GmailApiConfigurationSection = "GmailApi";
    private const string GoogleClientIdSuffix = ".apps.googleusercontent.com";
    private const string AuthenticationLimitsMessage = "Authentication limits are invalid.";
    private const string UnsupportedAuthenticationMessage = "Select a supported authentication store and email provider.";
    private const string ProductionConfigurationMessage = "Production authentication requires PostgreSQL, a real email transport, a 32-character or longer OTP secret and an absolute persistent Data Protection path.";
    private const string ConnectionStringMessage = "Configure ConnectionStrings:BikeHub for authentication storage.";
    private const string SmtpConfigurationMessage = "Configure a valid SMTP host, port, TLS mode, username, app password and sender address.";
    private const string GmailConfigurationMessage = "Configure a Google OAuth client, refresh token and sender address for Gmail API.";
    private const string AuthCookieName = "BikeHub.Auth";
    private const string SecureAuthCookieName = "__Host-BikeHub.Auth";
    private const string CsrfHeaderName = "X-CSRF-TOKEN";
    private const string CsrfCookieName = "BikeHub.Csrf";
    private const string SecureCsrfCookieName = "__Host-BikeHub.Csrf";
    private const string UnknownClientAddress = "unknown";
    private const string ModelBindingLogCategory = "Microsoft.AspNetCore.Mvc.ModelBinding";

    public static WebApplicationBuilder AddBikeHubAuthentication(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(ConfigurationSection);
        var smtpSection = builder.Configuration.GetSection(SmtpConfigurationSection);
        var gmailApiSection = builder.Configuration.GetSection(GmailApiConfigurationSection);
        var smtpOptions = smtpSection.Get<SmtpOptions>() ?? new();
        var authOptions = section.Get<AuthOptions>() ?? new();
        var gmailApiOptions = gmailApiSection.Get<GmailApiOptions>() ?? new();
        var development = builder.Environment.IsDevelopment();
        if (authOptions.Enabled)
        {
            if (authOptions.CodeMinutes is < 1 or > 15 || authOptions.MaxAttempts is < 1 or > 10 ||
                authOptions.ResendSeconds is < 1 or > 3600 || authOptions.SessionDays is < 1 or > 30 ||
                authOptions.MaxPerEmailHour is < 1 or > 20 || authOptions.MaxPerDay is < 1 or > 10_000)
            {
                throw new InvalidOperationException(AuthenticationLimitsMessage);
            }

            if (authOptions.Store is not (Constants.AuthenticationStores.Memory or Constants.AuthenticationStores.Postgres) ||
                authOptions.EmailProvider is not (Constants.EmailProviders.Development or Constants.EmailProviders.Smtp or Constants.EmailProviders.GmailApi))
            {
                throw new InvalidOperationException(UnsupportedAuthenticationMessage);
            }

            if (!development && (authOptions.Store != Constants.AuthenticationStores.Postgres || authOptions.EmailProvider == Constants.EmailProviders.Development ||
                                 authOptions.OtpSecret.Length < 32 || !Path.IsPathFullyQualified(authOptions.DataProtectionPath)))
            {
                throw new InvalidOperationException(ProductionConfigurationMessage);
            }

            if (authOptions.Store == Constants.AuthenticationStores.Postgres &&
                string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(BikeHub.Dal.Constants.ConnectionStringName)))
            {
                throw new InvalidOperationException(ConnectionStringMessage);
            }

            if (authOptions.EmailProvider == Constants.EmailProviders.Smtp && (string.IsNullOrWhiteSpace(smtpOptions.Host) ||
                smtpOptions.Port is < 1 or > 65535 || smtpOptions.Security is not (Constants.SmtpSecurity.StartTls or Constants.SmtpSecurity.SslOnConnect) ||
                string.IsNullOrWhiteSpace(smtpOptions.Username) || string.IsNullOrWhiteSpace(smtpOptions.Password) ||
                OtpSecurity.NormalizeEmail(smtpOptions.FromAddress) is null))
            {
                throw new InvalidOperationException(SmtpConfigurationMessage);
            }

            if (authOptions.EmailProvider == Constants.EmailProviders.GmailApi && (string.IsNullOrWhiteSpace(gmailApiOptions.ClientId) ||
                !gmailApiOptions.ClientId.EndsWith(GoogleClientIdSuffix, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(gmailApiOptions.ClientSecret) || string.IsNullOrWhiteSpace(gmailApiOptions.RefreshToken) ||
                OtpSecurity.NormalizeEmail(gmailApiOptions.FromAddress) is null))
            {
                throw new InvalidOperationException(GmailConfigurationMessage);
            }
        }
        builder.Services.Configure<AuthOptions>(section);
        builder.Services.Configure<SmtpOptions>(smtpSection);
        builder.Services.Configure<GmailApiOptions>(gmailApiSection);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<OtpSecurity>();
        builder.Services.AddSingleton<DevelopmentMessages>();
        builder.Services.AddSingleton<IAuthenticationStore>(services =>
            authOptions.Enabled && authOptions.Store == Constants.AuthenticationStores.Postgres
                ? new PostgresAuthenticationStore(services.GetRequiredService<IDbContextFactory<BikeHubDbContext>>())
                : new InMemoryAuthenticationStore());
        builder.Services.AddSingleton<Google.Apis.Http.IHttpClientFactory, Google.Apis.Http.HttpClientFactory>();
        builder.Services.AddSingleton<IVerificationEmailSender>(services => authOptions.EmailProvider switch
        {
            Constants.EmailProviders.Development => ActivatorUtilities.CreateInstance<DevelopmentEmailSender>(services),
            Constants.EmailProviders.GmailApi => ActivatorUtilities.CreateInstance<GmailApiVerificationEmailSender>(services),
            _ => ActivatorUtilities.CreateInstance<SmtpVerificationEmailSender>(services)
        });
        builder.Services.AddSingleton<ISmsSender, TestSmsSender>();
        builder.Services.AddScoped<EmailAuthenticationService>();
        builder.Services.AddScoped<SessionCookieEvents>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddHostedService<AuthenticationCleanup>();

        var protection = builder.Services.AddDataProtection().SetApplicationName(Constants.AuthenticationName);
        if (string.IsNullOrWhiteSpace(authOptions.DataProtectionPath))
        {
            protection.UseEphemeralDataProtectionProvider();
        }
        else
        {
            var directory = new DirectoryInfo(authOptions.DataProtectionPath);
            if (!directory.Exists)
            {
                if (OperatingSystem.IsWindows())
                {
                    directory.Create();
                }
                else
                {
                    Directory.CreateDirectory(directory.FullName,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }
            protection.PersistKeysToFileSystem(directory);
        }

        var secure = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        builder.Services.AddAuthentication(Constants.AuthenticationScheme).AddCookie(Constants.AuthenticationScheme, options =>
        {
            options.Cookie.Name = development ? AuthCookieName : SecureAuthCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = secure;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = Constants.CookiePath;
            options.SlidingExpiration = false;
            options.ExpireTimeSpan = TimeSpan.FromDays(authOptions.SessionDays);
            options.EventsType = typeof(SessionCookieEvents);
        });
        builder.Services.AddAuthorization(options =>
            options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(Constants.AuthenticationScheme)
                .RequireAuthenticatedUser().RequireClaim(Constants.Claims.EmailVerified, Constants.Claims.VerifiedValue).Build());
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfHeaderName;
            options.Cookie.Name = development || !authOptions.Enabled ? CsrfCookieName : SecureCsrfCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.Path = Constants.CookiePath;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = authOptions.Enabled ? secure : CookieSecurePolicy.SameAsRequest;
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Constants.RateLimitPolicies.RequestCode, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? UnknownClientAddress, _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy(Constants.RateLimitPolicies.VerifyCode, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? UnknownClientAddress, _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        builder.Logging.AddFilter(ModelBindingLogCategory, LogLevel.Warning);
        return builder;
    }
}

public sealed class AuthenticationCleanup(IAuthenticationStore store, TimeProvider clock,
    IOptions<AuthOptions> options, ILogger<AuthenticationCleanup> logger) : BackgroundService
{
    private const string CleanupFailureLog = "Authentication cleanup failed ({FailureType})";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await store.CleanupAsync(clock.GetUtcNow(), stoppingToken); }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning(CleanupFailureLog, error.GetType().Name); }
        }
    }
}
