using BikeHub.Dal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = Constants.AdminPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminController(IDbContextFactory<BikeHubDbContext> contexts) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<AdminOverview>> Overview(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var accounts = await db.Accounts.CountAsync(cancellationToken);
        var blocked = await db.Accounts.CountAsync(a => a.IsBlocked, cancellationToken);
        var reports = await db.Reports.CountAsync(r => !r.Resolved, cancellationToken);
        return Ok(new AdminOverview(accounts, blocked, reports));
    }
}

public sealed record AdminOverview(int Accounts, int BlockedAccounts, int OpenReports);
