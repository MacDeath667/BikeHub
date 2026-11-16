using BikeHub.Api.Services;
using BikeHub.Application.Community;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BikeHub.Api.Controllers;

[ApiController, Authorize]
[Route("api/notifications")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationsController(NotificationService service) : ControllerBase
{
    [HttpGet]
    public ActionResult<Page<NotificationDto>> List([FromQuery] PaginationQuery page) => Ok(service.List(page.Page, page.PageSize));

    [HttpGet("unread-count")]
    public ActionResult<int> UnreadCount() => Ok(service.UnreadCount());

    [HttpPut("{id:guid}/read")]
    public IActionResult Read(Guid id)
    {
        service.Read(id);
        return NoContent();
    }

    [HttpPut("read")]
    public IActionResult ReadAll()
    {
        service.Read(null);
        return NoContent();
    }
}
