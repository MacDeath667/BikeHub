using BikeHub.Application.Community;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BikeHub.Api.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(IUserService service) : ControllerBase
{
    [HttpGet("me")]
    [Authorize]
    public ActionResult<UserDto> Me() => Ok(service.GetCurrentUser());

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDto>> Get(Guid id, CancellationToken cancellationToken) =>
        await service.GetUserAsync(id, cancellationToken) is { } user ? Ok(user) : NotFound();

    [Authorize]
    [HttpPut("{id:guid}/follow")]
    public ActionResult<UserDto> Follow(Guid id) => service.SetFollowing(id, true) is { } user ? Ok(user) : NotFound();

    [Authorize]
    [HttpDelete("{id:guid}/follow")]
    public ActionResult<UserDto> Unfollow(Guid id) => service.SetFollowing(id, false) is { } user ? Ok(user) : NotFound();
}
