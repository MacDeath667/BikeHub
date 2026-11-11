using BikeHub.Api.Services;
using BikeHub.Application.Community;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BikeHub.Api.Controllers;

[ApiController, Authorize, Route("api/routes")]
public sealed class RoutesController : ControllerBase
{
    [HttpPost("import"), Consumes("multipart/form-data")]
    [RequestSizeLimit(RouteService.MaximumFileBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = RouteService.MaximumFileBytes + 65536)]
    public ActionResult<MapRoute> Import(IFormFile file)
    {
        if (file.Length is <= 0 or > RouteService.MaximumFileBytes || !string.Equals(Path.GetExtension(file.FileName), ".gpx", StringComparison.OrdinalIgnoreCase))
        {
            return Problem(statusCode: 400, title: "Выберите GPX-файл до 5 МБ.");
        }
        using var input = file.OpenReadStream();
        return Ok(RouteService.Import(input));
    }
}
