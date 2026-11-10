using BikeHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BikeHub.Api.Controllers;

[ApiController]
[Route("api/media")]
public sealed class MediaController(MediaStore store) : ControllerBase
{
    [Authorize]
    [HttpPost]
    [RequestSizeLimit(MediaStore.MaxUploadBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = MediaStore.MaxUploadBytes + 65536)]
    public async Task<ActionResult<MediaDto>> Upload(IFormFile file, CancellationToken cancellationToken) =>
        Ok(await store.UploadAsync(file, cancellationToken));

    [HttpGet("{id:guid}")]
    public IActionResult Get(Guid id)
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return store.Read(id) is { } path ? PhysicalFile(path, "image/webp") : NotFound();
    }
}
