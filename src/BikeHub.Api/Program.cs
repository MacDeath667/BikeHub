using BikeHub.Api.Middleware;
using BikeHub.Api.Authentication;
using BikeHub.Api.Observability;
using BikeHub.Api.Services;
using BikeHub.Application.Community;
using BikeHub.Dal;

var migrationsOnly = args.Contains("--migrate-only", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(
    args.Where(argument => !string.Equals(argument, "--migrate-only", StringComparison.Ordinal)).ToArray());

var bootstrapEnabled = builder.Configuration.GetValue("Database:BootstrapEnabled", true);
var migrationsEnabled = builder.Configuration.GetValue("Database:MigrationsEnabled", true);
if (bootstrapEnabled || migrationsEnabled || migrationsOnly)
{
    builder.AddDapper("BikeHub");
    builder.Services.AddSingleton<DatabaseMigrator>();
    builder.Services.AddSingleton<DatabaseBootstrapper>();
}
builder.Services.AddControllers(options => options.Filters.Add<CsrfValidationFilter>());
builder.Services.AddSingleton<MockCommunityStore>();
builder.Services.AddScoped<IFeedService, MockFeedService>();
builder.Services.AddScoped<IBikeService, MockBikeService>();
builder.Services.AddScoped<IPostService, MockPostService>();
builder.Services.AddScoped<IUserService, MockUserService>();
builder.Services.AddScoped<ICommunityService, MockCommunityService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

if (!migrationsOnly)
{
    builder.AddBikeHubAuthentication();
    builder.AddBikeHubReverseProxy();
    builder.AddBikeHubObservability();
}


var app = builder.Build();
if (!migrationsOnly) app.UseForwardedHeaders();
app.UseMiddleware<StatisticsMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
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
    return;

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", dataSource = "in-memory-mocks" }));
// Keep unknown API routes as 404 rather than serving the SPA document.
app.MapFallback("/api/{**path}", () => Results.NotFound());
if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html")))
    app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
