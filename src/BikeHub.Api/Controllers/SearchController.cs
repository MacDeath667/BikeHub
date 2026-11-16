using BikeHub.Api.Services;
using BikeHub.Application.Community;
using Microsoft.AspNetCore.Mvc;

namespace BikeHub.Api.Controllers;

[ApiController, Route("api/search")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SearchController(SearchService search, GroupService groups) : ControllerBase
{
    [HttpGet("posts")]
    public ActionResult<Page<PostDto>> Posts([FromQuery] string? q, [FromQuery] PaginationQuery paging) =>
        Ok(search.Posts(q, paging.Page, paging.PageSize));

    [HttpGet("people")]
    public ActionResult<Page<UserDto>> People([FromQuery] string? q, [FromQuery] PaginationQuery paging) =>
        Ok(search.People(q, paging.Page, paging.PageSize));

    [HttpGet("communities")]
    public ActionResult<Page<CommunityDto>> Communities([FromQuery] string? q, [FromQuery] PaginationQuery paging) =>
        Ok(search.Communities(q, paging.Page, paging.PageSize));

    [HttpGet("discussions")]
    public ActionResult<Page<GroupEntryDto>> Discussions([FromQuery] string? q, [FromQuery] PaginationQuery paging) =>
        Ok(groups.Search(q, paging.Page, paging.PageSize));
}
