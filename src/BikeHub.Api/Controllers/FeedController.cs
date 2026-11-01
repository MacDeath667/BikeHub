using BikeHub.Application.Community;
using Microsoft.AspNetCore.Mvc;
using BikeHub.Api.Authentication;

namespace BikeHub.Api.Controllers;

[ApiController]
[Route("api/feed")]
public sealed class FeedController(IFeedService service, ICurrentUser current) : ControllerBase
{
    [HttpGet]
    public ActionResult<Page<PostDto>> Get([FromQuery] PaginationQuery paging,
        [FromQuery] string? tab, [FromQuery] BikeCategory? category, [FromQuery] string? q)
    {
        if (tab is Constants.FeedTabs.Saved or Constants.FeedTabs.Following && current.Id is null)
        {
            return Unauthorized();
        }

        return Ok(service.GetFeed(tab, category, q, paging.Page, paging.PageSize));
    }

    [HttpGet("overview")]
    public ActionResult<OverviewDto> Overview() => Ok(service.GetOverview());
}
