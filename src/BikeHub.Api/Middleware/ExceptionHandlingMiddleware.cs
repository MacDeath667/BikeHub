using System.Diagnostics;

namespace BikeHub.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            if (context.Request.Path.StartsWithSegments("/api/auth"))
                logger.LogError("Authentication request failed ({FailureType})", exception.GetType().Name);
            else logger.LogError(
                exception,
                "Unhandled exception for {Method} {Path}",
                context.Request.Method,
                context.Request.Path);

            context.Response.StatusCode =
                StatusCodes.Status500InternalServerError;

            await Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error",
                    extensions: new Dictionary<string, object?>
                    {
                        ["traceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier
                    })
                .ExecuteAsync(context);
        }
    }
}
