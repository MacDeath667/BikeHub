using BikeHub.Application.Community;
using Microsoft.AspNetCore.Mvc;

namespace BikeHub.Api.Controllers;

[ApiController]
[Route("api/feed")]
public sealed class FeedController(IFeedService service) : ControllerBase
{
    [HttpGet]
    public ActionResult<Page<PostDto>> Get([FromQuery] PaginationQuery paging,
        [FromQuery] string? tab, [FromQuery] string? category, [FromQuery] string? q) =>
        Ok(service.GetFeed(tab, category, q, paging.Page, paging.PageSize));

    [HttpGet("overview")]
    public ActionResult<OverviewDto> Overview() => Ok(service.GetOverview());
}
