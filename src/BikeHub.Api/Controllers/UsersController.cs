using BikeHub.Application.Community;
using Microsoft.AspNetCore.Mvc;

namespace BikeHub.Api.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(IUserService service) : ControllerBase
{
    [HttpGet("me")]
    public ActionResult<UserDto> Me() => Ok(service.GetCurrentUser());

    [HttpGet("{id:guid}")]
    public ActionResult<UserDto> Get(Guid id) => service.GetUser(id) is { } user ? Ok(user) : NotFound();

    [HttpPut("{id:guid}/follow")]
    public ActionResult<UserDto> Follow(Guid id) => service.SetFollowing(id, true) is { } user ? Ok(user) : NotFound();

    [HttpDelete("{id:guid}/follow")]
    public ActionResult<UserDto> Unfollow(Guid id) => service.SetFollowing(id, false) is { } user ? Ok(user) : NotFound();
}
