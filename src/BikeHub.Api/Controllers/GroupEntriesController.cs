using BikeHub.Api.Services;
using BikeHub.Application.Community;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BikeHub.Api.Controllers;

[ApiController, Route("api/group-entries")]
public sealed class GroupEntriesController(GroupService groups) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public ActionResult<GroupEntryDto> Get(Guid id) => Ok(groups.Entry(id));
    [HttpGet("{id:guid}/route-preview")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult<MapRoute> RoutePreview(Guid id) => groups.Entry(id).Route is { } route ? Ok(RouteService.Preview(route)) : NotFound();
    [Authorize, HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id) { groups.DeleteEntry(id); return NoContent(); }
    [HttpGet("{id:guid}/replies")]
    public ActionResult<Page<CommentDto>> Replies(Guid id, [FromQuery] int page = 1) => Ok(groups.Replies(id, page));
    [Authorize, HttpPost("{id:guid}/replies")]
    public ActionResult<CommentDto> Reply(Guid id, CreateCommentRequest request) => Ok(groups.Reply(id, request));
    [Authorize, HttpDelete("{id:guid}/replies/{replyId:guid}")]
    public IActionResult DeleteReply(Guid id, Guid replyId) { groups.DeleteReply(id, replyId); return NoContent(); }
}
