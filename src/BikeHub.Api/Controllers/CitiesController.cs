using System.ComponentModel.DataAnnotations;
using BikeHub.Dal.ReferenceData;
using Microsoft.AspNetCore.Mvc;

namespace BikeHub.Api.Controllers;

[ApiController]
[Route("api/cities")]
public sealed class CitiesController : ControllerBase
{
    [HttpGet]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public ActionResult<IReadOnlyList<CityOption>> Search([FromQuery, StringLength(CityCatalog.MaximumQueryLength)] string? q) => Ok(CityCatalog.Search(q));
}
