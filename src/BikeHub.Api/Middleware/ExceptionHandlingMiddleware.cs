using System.Diagnostics;

namespace BikeHub.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    private const string AuthenticationFailureLog = "Authentication request failed ({FailureType})";
    private const string UnhandledExceptionLog = "Unhandled exception for {Method} {Path}";
    private const string InternalServerErrorTitle = "Internal Server Error";
    private const string TraceIdKey = "traceId";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            if (context.Request.Path.StartsWithSegments("/api/auth"))
            {
                logger.LogError(AuthenticationFailureLog, exception.GetType().Name);
            }
            else
            {
                logger.LogError(
                exception,
                UnhandledExceptionLog,
                context.Request.Method,
                context.Request.Path);
            }

            context.Response.StatusCode =
                StatusCodes.Status500InternalServerError;

            await Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: InternalServerErrorTitle,
                    extensions: new Dictionary<string, object?>
                    {
                        [TraceIdKey] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier
                    })
                .ExecuteAsync(context);
        }
    }
}
