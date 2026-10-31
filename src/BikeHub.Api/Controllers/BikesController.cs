using BikeHub.Application.Community;
using Microsoft.AspNetCore.Mvc;

namespace BikeHub.Api.Controllers;

[ApiController]
[Route("api/bikes")]
public sealed class BikesController(IBikeService service) : ControllerBase
{
    [HttpGet]
    public ActionResult<Page<BikeDto>> Get([FromQuery] PaginationQuery paging,
        [FromQuery] string? q, [FromQuery] string? category, [FromQuery] Guid? ownerId) =>
        Ok(service.GetBikes(q, category, ownerId, paging.Page, paging.PageSize));

    [HttpGet("{id:guid}")]
    public ActionResult<BikeDto> GetById(Guid id) => service.GetBike(id) is { } bike ? Ok(bike) : NotFound();

    [HttpGet("{id:guid}/history")]
    public ActionResult<Page<PostDto>> History(Guid id, [FromQuery] PaginationQuery paging) =>
        service.GetHistory(id, paging.Page, paging.PageSize) is { } history ? Ok(history) : NotFound();

    [HttpGet("/api/users/{ownerId:guid}/bikes")]
    [HttpGet("/api/user/{ownerId:guid}/bikes")]
    public ActionResult<Page<BikeDto>> ForUser(Guid ownerId, [FromQuery] PaginationQuery paging) =>
        Ok(service.GetBikes(null, null, ownerId, paging.Page, paging.PageSize));

    [HttpPost]
    public ActionResult<BikeDto> Create(CreateBikeRequest request)
    {
        var bike = service.CreateBike(request);
        return CreatedAtAction(nameof(GetById), new { id = bike.Id }, bike);
    }
}
