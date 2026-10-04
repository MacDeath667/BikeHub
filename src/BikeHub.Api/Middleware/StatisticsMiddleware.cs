using System.Diagnostics;

namespace BikeHub.Api.Middleware;

public sealed class StatisticsMiddleware(
    RequestDelegate next,
    ILogger<StatisticsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            await next(context);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);

            logger.LogInformation(
                "HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs} ms",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                elapsed.TotalMilliseconds);
        }
    }
}