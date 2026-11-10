using BikeHub.Api.Services;
using BikeHub.Application.Community;
using BikeHub.Dal.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BikeHub.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class ContentManagementController(PostgresCommunityService service) : ControllerBase
{
    [HttpPut("bikes/{id:guid}")]
    public ActionResult<BikeDto> UpdateBike(Guid id, CreateBikeRequest request) => Ok(service.UpdateBike(id, request));

    [HttpDelete("bikes/{id:guid}")]
    public IActionResult DeleteBike(Guid id)
    {
        service.DeleteBike(id);
        return NoContent();
    }

    [HttpPut("posts/{id:guid}")]
    public ActionResult<PostDto> UpdatePost(Guid id, CreatePostRequest request) => Ok(service.UpdatePost(id, request));

    [HttpDelete("posts/{id:guid}")]
    public IActionResult DeletePost(Guid id)
    {
        service.DeletePost(id);
        return NoContent();
    }

    [HttpPut("posts/{postId:guid}/comments/{id:guid}")]
    public ActionResult<CommentDto> UpdateComment(Guid postId, Guid id, CreateCommentRequest request) => Ok(service.UpdateComment(postId, id, request));

    [HttpDelete("posts/{postId:guid}/comments/{id:guid}")]
    public IActionResult DeleteComment(Guid postId, Guid id)
    {
        service.DeleteComment(postId, id);
        return NoContent();
    }

    [HttpPut("users/me")]
    public ActionResult<UserDto> UpdateProfile(UpdateProfileRequest request) => Ok(service.UpdateProfile(request));

    [HttpPost("reports")]
    public ActionResult<ReportDto> Report(ReportRequest request) => Ok(service.Report(request));

    [HttpGet("moderation/reports")]
    public ActionResult<Page<ReportDto>> Reports([FromQuery] PaginationQuery paging, bool resolved = false) =>
        Ok(service.Reports(resolved, paging.Page, paging.PageSize));

    [HttpPost("moderation/reports/{id:guid}/resolve")]
    public IActionResult Resolve(Guid id, ResolveReportRequest request)
    {
        service.ResolveReport(id, request.Resolution);
        return NoContent();
    }

    [HttpPut("moderation/{type}/{id:guid}/visibility")]
    public IActionResult Visibility(ReportTarget type, Guid id, VisibilityRequest request)
    {
        service.SetVisibility(type, id, request.Hidden);
        return NoContent();
    }

    [HttpPut("moderation/users/{id:guid}/block")]
    public IActionResult Block(Guid id, BlockUserRequest request)
    {
        service.BlockUser(id, request.Blocked);
        return NoContent();
    }
}
