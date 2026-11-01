using BikeHub.Application.Community;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BikeHub.Api.Controllers;

[ApiController]
[Route("api/posts")]
public sealed class PostsController(IPostService service) : ControllerBase
{
    private const string InvalidBikeTitle = "Выберите велосипед из своего гаража.";

    [HttpGet("{id:guid}")]
    public ActionResult<PostDto> Get(Guid id) => service.GetPost(id) is { } post ? Ok(post) : NotFound();

    [Authorize]
    [HttpPost]
    public ActionResult<PostDto> Create(CreatePostRequest request)
    {
        var post = service.CreatePost(request);
        return post is null
            ? Problem(statusCode: StatusCodes.Status400BadRequest, title: InvalidBikeTitle)
            : CreatedAtAction(nameof(Get), new { id = post.Id }, post);
    }

    [Authorize]
    [HttpPut("{id:guid}/like")]
    public ActionResult<PostDto> Like(Guid id) => service.SetLike(id, true) is { } post ? Ok(post) : NotFound();

    [Authorize]
    [HttpDelete("{id:guid}/like")]
    public ActionResult<PostDto> Unlike(Guid id) => service.SetLike(id, false) is { } post ? Ok(post) : NotFound();

    [Authorize]
    [HttpPut("{id:guid}/bookmark")]
    public ActionResult<PostDto> Bookmark(Guid id) => service.SetBookmark(id, true) is { } post ? Ok(post) : NotFound();

    [Authorize]
    [HttpDelete("{id:guid}/bookmark")]
    public ActionResult<PostDto> RemoveBookmark(Guid id) => service.SetBookmark(id, false) is { } post ? Ok(post) : NotFound();

    [HttpGet("{id:guid}/comments")]
    public ActionResult<Page<CommentDto>> Comments(Guid id, [FromQuery] PaginationQuery paging) =>
        service.GetComments(id, paging.Page, paging.PageSize) is { } comments ? Ok(comments) : NotFound();

    [Authorize]
    [HttpPost("{id:guid}/comments")]
    public ActionResult<CommentDto> AddComment(Guid id, CreateCommentRequest request) =>
        service.AddComment(id, request) is { } comment
            ? Created($"/api/posts/{id}/comments", comment)
            : NotFound();
}
