using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace BikeHub.Api.Authentication;

public sealed class SessionCookieEvents(IAuthenticationStore store, TimeProvider clock,
    IOptions<AuthOptions> options, ILogger<SessionCookieEvents> logger) : CookieAuthenticationEvents
{
    private const string SessionStorageFailureLog = "Authentication session storage unavailable ({FailureType})";

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (!options.Value.Enabled ||
            !Guid.TryParse(context.Principal?.FindFirstValue(Constants.Claims.SessionId), out var sessionId) ||
            !Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
        {
            context.RejectPrincipal();
            return;
        }
        try
        {
            var session = await store.GetSessionAsync(sessionId, clock.GetUtcNow(), context.HttpContext.RequestAborted);
            if (session is null || session.Account.Id != accountId)
            {
                context.RejectPrincipal();
            }
            else
            {
                context.HttpContext.Items[typeof(AuthSession)] = session;
            }
        }
        catch (DbException error)
        {
            logger.LogWarning(SessionStorageFailureLog, error.GetType().Name);
            context.RejectPrincipal();
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public AuthAccount? Account => (accessor.HttpContext?.Items[typeof(AuthSession)] as AuthSession)?.Account;
    public Guid? Id => Account?.Id;
}
