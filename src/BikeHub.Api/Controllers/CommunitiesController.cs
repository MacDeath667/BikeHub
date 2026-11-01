using BikeHub.Application.Community;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BikeHub.Api.Controllers;

[ApiController]
[Route("api/communities")]
public sealed class CommunitiesController(ICommunityService service) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<CommunityDto>> Get([FromQuery] string? q) => Ok(service.GetCommunities(q));

    [Authorize]
    [HttpPut("{id:guid}/membership")]
    public ActionResult<CommunityDto> Join(Guid id) => service.SetMembership(id, true) is { } community ? Ok(community) : NotFound();

    [Authorize]
    [HttpDelete("{id:guid}/membership")]
    public ActionResult<CommunityDto> Leave(Guid id) => service.SetMembership(id, false) is { } community ? Ok(community) : NotFound();
}
