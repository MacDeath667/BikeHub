using BikeHub.Api.Services;
using BikeHub.Application.Community;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BikeHub.Api.Controllers;

[ApiController, Authorize, Route("api/bikes/{bikeId:guid}/maintenance")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MaintenanceController(MaintenanceService maintenance) : ControllerBase
{
    [HttpGet]
    public ActionResult<MaintenanceOverviewDto> Overview(Guid bikeId) => Ok(maintenance.Overview(bikeId));
    [HttpPut("mileage")]
    public ActionResult<MaintenanceOverviewDto> Mileage(Guid bikeId, UpdateMileageRequest request) => Ok(maintenance.Mileage(bikeId, request.Mileage));
    [HttpGet("components")]
    public ActionResult<Page<BikeComponentDto>> Components(Guid bikeId, [FromQuery] PaginationQuery paging, [FromQuery] bool archived = false) =>
        Ok(maintenance.Components(bikeId, archived, paging.Page, paging.PageSize));
    [HttpGet("components/options")]
    public ActionResult<IReadOnlyList<ComponentOptionDto>> Options(Guid bikeId) => Ok(maintenance.ComponentOptions(bikeId));
    [HttpPost("components")]
    public ActionResult<BikeComponentDto> AddComponent(Guid bikeId, SaveBikeComponentRequest request) => Ok(maintenance.SaveComponent(bikeId, null, request));
    [HttpPut("components/{id:guid}")]
    public ActionResult<BikeComponentDto> UpdateComponent(Guid bikeId, Guid id, SaveBikeComponentRequest request) => Ok(maintenance.SaveComponent(bikeId, id, request));
    [HttpGet("records")]
    public ActionResult<Page<MaintenanceRecordDto>> Records(Guid bikeId, [FromQuery] PaginationQuery paging, [FromQuery] Guid? componentId) => Ok(maintenance.History(bikeId, componentId, paging.Page, paging.PageSize));
    [HttpPost("records")]
    public ActionResult<MaintenanceRecordDto> AddRecord(Guid bikeId, SaveMaintenanceRecordRequest request) => Ok(maintenance.SaveRecord(bikeId, null, request));
    [HttpPut("records/{id:guid}")]
    public ActionResult<MaintenanceRecordDto> UpdateRecord(Guid bikeId, Guid id, SaveMaintenanceRecordRequest request) => Ok(maintenance.SaveRecord(bikeId, id, request));
    [HttpDelete("records/{id:guid}")]
    public IActionResult DeleteRecord(Guid bikeId, Guid id) { maintenance.DeleteRecord(bikeId, id); return NoContent(); }
}
