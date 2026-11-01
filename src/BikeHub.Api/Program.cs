using Constants = BikeHub.Api.Constants;
using BikeHub.Api.Middleware;
using BikeHub.Api.Authentication;
using BikeHub.Api.Observability;
using BikeHub.Api.Services;
using BikeHub.Application.Community;
using BikeHub.Dal;

var migrationsOnly = args.Contains(Constants.MigrateOnlyArgument, StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(
    args.Where(argument => !string.Equals(argument, Constants.MigrateOnlyArgument, StringComparison.Ordinal)).ToArray());

var bootstrapEnabled = builder.Configuration.GetValue(Program.DatabaseBootstrapEnabledKey, true);
var migrationsEnabled = builder.Configuration.GetValue(Program.DatabaseMigrationsEnabledKey, true);
builder.AddBikeHubDatabase();

if (!migrationsOnly)
{
    builder.Services.AddControllers(options => options.Filters.Add<CsrfValidationFilter>());
    builder.Services.AddSingleton<MockCommunityStore>();
    builder.Services.AddScoped<IFeedService, MockFeedService>();
    builder.Services.AddScoped<IBikeService, MockBikeService>();
    builder.Services.AddScoped<IPostService, MockPostService>();
    builder.Services.AddScoped<IUserService, MockUserService>();
    builder.Services.AddScoped<ICommunityService, MockCommunityService>();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.AddBikeHubAuthentication();
    builder.AddBikeHubReverseProxy();
    builder.AddBikeHubObservability();
}


var app = builder.Build();
if (!migrationsOnly)
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<StatisticsMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!migrationsOnly && app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (bootstrapEnabled)
{
    await app.Services
        .GetRequiredService<DatabaseBootstrapper>()
        .EnsureDatabaseAsync(CancellationToken.None);
}

if (migrationsOnly || migrationsEnabled)
{
    await app.Services
        .GetRequiredService<DatabaseMigrator>()
        .MigrateAsync(CancellationToken.None);
}


if (migrationsOnly)
{
    return;
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = Program.HealthyStatus, dataSource = Program.MockDataSource }));
// Keep unknown API routes as 404 rather than serving the SPA document.
app.MapFallback("/api/{**path}", () => Results.NotFound());


app.Run();

public partial class Program
{
    private const string DatabaseBootstrapEnabledKey = "Database:BootstrapEnabled";
    private const string DatabaseMigrationsEnabledKey = "Database:MigrationsEnabled";
    private const string HealthyStatus = "ok";
    private const string MockDataSource = "in-memory-mocks";
}
