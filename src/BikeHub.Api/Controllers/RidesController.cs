using BikeHub.Api.Services;
using BikeHub.Application.Community;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BikeHub.Api.Controllers;

[ApiController, Route("api/rides")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RidesController(RideService rides) : ControllerBase
{
    [HttpGet]
    public ActionResult<Page<RideDto>> List([FromQuery] PaginationQuery paging, [FromQuery] string? city,
        [FromQuery] Guid? groupId, [FromQuery] bool mine = false, [FromQuery] bool past = false) =>
        Ok(rides.List(city, groupId, mine, past, paging.Page, paging.PageSize));
    [HttpGet("{id:guid}")]
    public ActionResult<RideDto> Get(Guid id) => Ok(rides.Get(id));
    [HttpGet("{id:guid}/participants")]
    public ActionResult<Page<RideParticipantDto>> Participants(Guid id, [FromQuery] PaginationQuery paging) => Ok(rides.Members(id, paging.Page, paging.PageSize));
    [Authorize, HttpPost]
    public ActionResult<RideDto> Create(SaveRideRequest request)
    {
        var ride = rides.Save(null, request); return CreatedAtAction(nameof(Get), new { id = ride.Id }, ride);
    }
    [Authorize, HttpPut("{id:guid}")]
    public ActionResult<RideDto> Update(Guid id, SaveRideRequest request) => Ok(rides.Save(id, request));
    [Authorize, HttpPost("{id:guid}/cancel")]
    public ActionResult<RideDto> Cancel(Guid id, RideRevisionRequest request) => Ok(rides.Cancel(id, request.Revision));
    [Authorize, HttpPut("{id:guid}/attendance")]
    public ActionResult<RideDto> Attend(Guid id, RideAttendanceRequest request) => Ok(rides.Attend(id, request.Attendance));
    [Authorize, HttpDelete("{id:guid}/attendance")]
    public ActionResult<RideDto> Leave(Guid id) => Ok(rides.Attend(id, null));
    [Authorize, HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id) { rides.Delete(id); return NoContent(); }
}
