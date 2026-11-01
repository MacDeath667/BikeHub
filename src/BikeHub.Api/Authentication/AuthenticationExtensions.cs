using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using BikeHub.Dal;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Authentication;

public static class AuthenticationExtensions
{
    private const string ConfigurationSection = "Authentication";
    private const string AuthenticationLimitsMessage = "Authentication limits are invalid.";
    private const string UnsupportedAuthenticationMessage = "Select a supported authentication store and email provider.";
    private const string ProductionConfigurationMessage = "Production authentication requires PostgreSQL, a real email transport, a 32-character or longer OTP secret and an absolute persistent Data Protection path.";
    private const string ConnectionStringMessage = "Configure ConnectionStrings:BikeHub for authentication storage.";
    private const string MailServiceConfigurationMessage = "Configure a valid MailService:BaseUrl and a 32-character or longer MailService:ApiKey.";
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
        var authOptions = section.Get<AuthOptions>() ?? new();
        var mailOptions = builder.Configuration.GetSection("MailService").Get<MailServiceOptions>() ?? new();
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
                authOptions.EmailProvider is not (Constants.EmailProviders.Development or Constants.EmailProviders.MailService))
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

            if (authOptions.EmailProvider == Constants.EmailProviders.MailService &&
                (!Uri.TryCreate(mailOptions.BaseUrl, UriKind.Absolute, out var endpoint) ||
                 endpoint.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(endpoint.UserInfo) || mailOptions.ApiKey.Length < 32))
            {
                throw new InvalidOperationException(MailServiceConfigurationMessage);
            }
        }
        builder.Services.Configure<AuthOptions>(section);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<OtpSecurity>();
        builder.Services.AddSingleton<DevelopmentMessages>();
        builder.Services.AddSingleton<IAuthenticationStore>(services =>
            authOptions.Enabled && authOptions.Store == Constants.AuthenticationStores.Postgres
                ? new PostgresAuthenticationStore(services.GetRequiredService<IDbContextFactory<BikeHubDbContext>>())
                : new InMemoryAuthenticationStore());
        if (authOptions.EmailProvider == Constants.EmailProviders.Development)
        {
            builder.Services.AddSingleton<IVerificationEmailSender, DevelopmentEmailSender>();
        }
        else
        {
            builder.Services.AddHttpClient<IVerificationEmailSender, MailServiceEmailSender>(client =>
                MailServiceEmailSender.Configure(client, mailOptions));
        }
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
