using System.Diagnostics;

namespace BikeHub.Api.Middleware;

public sealed class StatisticsMiddleware(
    RequestDelegate next,
    ILogger<StatisticsMiddleware> logger)
{
    private const string RequestCompletedLog = "HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs} ms";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path == "/api/health")
        {
            await next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();

        try
        {
            await next(context);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);

            logger.LogInformation(
                RequestCompletedLog,
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                elapsed.TotalMilliseconds);
        }
    }
}
