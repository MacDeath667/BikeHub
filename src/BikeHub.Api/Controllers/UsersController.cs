using BikeHub.Application.Community;
using BikeHub.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BikeHub.Api.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(IUserService service, NicknameService nicknames) : ControllerBase
{
    [HttpGet("by-nickname/{nickname}")]
    public ActionResult<UserDto> ByNickname(string nickname) => nicknames.Find(nickname) is { } user ? Ok(user) : NotFound();

    [HttpGet("nicknames")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult<IReadOnlyList<UserDto>> SearchNicknames([FromQuery] string? q) => Ok(nicknames.Search(q));

    [Authorize, HttpPost("me/nickname")]
    public ActionResult<UserDto> ChooseNickname(ChooseNicknameRequest request) => Ok(nicknames.Choose(request.Nickname));

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
