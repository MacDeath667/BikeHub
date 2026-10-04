using Microsoft.AspNetCore.Mvc;

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
            logger.LogError(
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
                        ["traceId"] = context.TraceIdentifier
                    })
                .ExecuteAsync(context);
        }
    }
}