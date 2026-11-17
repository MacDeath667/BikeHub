using System.ComponentModel.DataAnnotations;
using BikeHub.Api.Services;
using BikeHub.Application.Community;
using BikeHub.Dal.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BikeHub.Api.Controllers;

[ApiController, Route("api/route-library")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RouteLibraryController(RouteLibraryService routes) : ControllerBase
{
    [HttpGet]
    public ActionResult<Page<LibraryRouteDto>> List([FromQuery] PaginationQuery paging, [FromQuery] string? city, [FromQuery] RouteSurface? surface,
        [FromQuery, Range(0, 100000)] decimal? minimum, [FromQuery, Range(0, 100000)] decimal? maximum,
        [FromQuery] bool mine = false, [FromQuery] bool saved = false, [FromQuery] Guid? collectionId = null) =>
        Ok(routes.List(city, surface, minimum, maximum, mine, saved, collectionId, paging.Page, paging.PageSize));
    [HttpGet("{id:guid}")]
    public ActionResult<LibraryRouteDto> Get(Guid id) => Ok(routes.Get(id));
    [HttpGet("{id:guid}/preview")]
    public ActionResult<MapRoute> Preview(Guid id) => Ok(routes.Preview(id));
    [Authorize, HttpPost]
    public ActionResult<LibraryRouteDto> Create(SaveLibraryRouteRequest request)
    {
        var route = routes.Save(null, request); return CreatedAtAction(nameof(Get), new { id = route.Id }, route);
    }
    [Authorize, HttpPut("{id:guid}")]
    public ActionResult<LibraryRouteDto> Update(Guid id, SaveLibraryRouteRequest request) => Ok(routes.Save(id, request));
    [Authorize, HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id) { routes.Delete(id); return NoContent(); }
    [Authorize, HttpPut("{id:guid}/bookmark")]
    public ActionResult<LibraryRouteDto> Bookmark(Guid id, SaveRouteBookmarkRequest request) => Ok(routes.Bookmark(id, request));
    [Authorize, HttpDelete("{id:guid}/bookmark")]
    public ActionResult<LibraryRouteDto> Unsave(Guid id) => Ok(routes.Bookmark(id, null));
    [HttpGet("{id:guid}/reviews")]
    public ActionResult<Page<RouteReviewDto>> Reviews(Guid id, [FromQuery] PaginationQuery paging) => Ok(routes.ReviewList(id, paging.Page, paging.PageSize));
    [Authorize, HttpPut("{id:guid}/reviews/me")]
    public ActionResult<RouteReviewDto> Review(Guid id, SaveRouteReviewRequest request) => Ok(routes.Review(id, request));
    [Authorize, HttpDelete("{id:guid}/reviews/{reviewId:guid}")]
    public IActionResult DeleteReview(Guid id, Guid reviewId) { routes.DeleteReview(id, reviewId); return NoContent(); }
}

[ApiController, Authorize, Route("api/route-collections")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RouteCollectionsController(RouteLibraryService routes) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<RouteCollectionDto>> List() => Ok(routes.Collections());
    [HttpPost]
    public ActionResult<RouteCollectionDto> Create(RouteCollectionRequest request) => Ok(routes.SaveCollection(null, request));
    [HttpPut("{id:guid}")]
    public ActionResult<RouteCollectionDto> Update(Guid id, RouteCollectionRequest request) => Ok(routes.SaveCollection(id, request));
    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id) { routes.DeleteCollection(id); return NoContent(); }
}
