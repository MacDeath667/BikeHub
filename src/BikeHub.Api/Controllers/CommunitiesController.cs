using BikeHub.Application.Community;
using BikeHub.Api.Services;
using BikeHub.Dal.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BikeHub.Api.Controllers;

[ApiController]
[Route("api/communities")]
public sealed class CommunitiesController(ICommunityService service, GroupService groups) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<CommunityDto>> Get([FromQuery] string? q) => Ok(service.GetCommunities(q));

    [HttpGet("{id:guid}")]
    public ActionResult<CommunityDto> Detail(Guid id) => Ok(groups.Get(id));

    [HttpGet("{id:guid}/members")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult<Page<UserDto>> Members(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Ok(groups.Members(id, page, pageSize));

    [Authorize, HttpPost]
    public ActionResult<CommunityDto> Create(SaveGroupRequest request) => Ok(groups.Save(null, request));

    [Authorize, HttpPut("{id:guid}")]
    public ActionResult<CommunityDto> Update(Guid id, SaveGroupRequest request) => Ok(groups.Save(id, request));

    [Authorize, HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id) { groups.Delete(id); return NoContent(); }

    [HttpGet("{id:guid}/entries")]
    public ActionResult<Page<GroupEntryDto>> Entries(Guid id, [FromQuery] GroupEntryKind kind = GroupEntryKind.Post, [FromQuery] int page = 1) => Ok(groups.Entries(id, kind, page));

    [Authorize, HttpPost("{id:guid}/entries")]
    public ActionResult<GroupEntryDto> CreateEntry(Guid id, SaveGroupEntryRequest request) => Ok(groups.SaveEntry(id, null, request));

    [Authorize, HttpPut("{id:guid}/entries/{entryId:guid}")]
    public ActionResult<GroupEntryDto> UpdateEntry(Guid id, Guid entryId, SaveGroupEntryRequest request) => Ok(groups.SaveEntry(id, entryId, request));

    [Authorize]
    [HttpPut("{id:guid}/membership")]
    public ActionResult<CommunityDto> Join(Guid id) => service.SetMembership(id, true) is { } community ? Ok(community) : NotFound();

    [Authorize]
    [HttpDelete("{id:guid}/membership")]
    public ActionResult<CommunityDto> Leave(Guid id) => service.SetMembership(id, false) is { } community ? Ok(community) : NotFound();
}
