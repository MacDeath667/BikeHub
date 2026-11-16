using BikeHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BikeHub.Api.Controllers;

[ApiController, Authorize]
[Route("api/drafts/{scope}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DraftsController(DraftService service) : ControllerBase
{
    [HttpGet]
    public ActionResult<DraftDto> Get(string scope) => Ok(service.Get(scope));

    [HttpPut]
    public ActionResult<DraftDto> Save(string scope, SaveDraftRequest request) => Ok(service.Save(scope, request));

    [HttpDelete]
    public ActionResult<DraftDto> Clear(string scope, [FromQuery] long revision = 0) => Ok(service.Clear(scope, revision));
}
