using System.ComponentModel.DataAnnotations;
using BikeHub.Api.Authentication;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RouteCollection = BikeHub.Dal.Entities.RouteCollection;

namespace BikeHub.Api.Services;

public sealed record SaveLibraryRouteRequest([Required, StringLength(160, MinimumLength = 3)] string Title,
    [Required, StringLength(10000)] string Description, [Required, StringLength(100)] string City,
    [EnumDataType(typeof(RouteSurface))] RouteSurface Surface, [Required] MapRoute Route, long? Revision = null);
public sealed record RouteCollectionRequest([Required, StringLength(80, MinimumLength = 2)] string Name);
public sealed record SaveRouteBookmarkRequest([StringLength(2000)] string Note = "", Guid? CollectionId = null);
public sealed record SaveRouteReviewRequest([Range(1, 5)] int Rating, [Required, StringLength(2000)] string Text, DateOnly RiddenOn,
    [StringLength(100)] string? TimeZone = null);
public sealed record LibraryRouteDto(Guid Id, UserDto Author, string Title, string Description, string City, RouteSurface Surface,
    decimal DistanceKm, long Revision, DateTimeOffset CreatedAt, int ReviewsCount, double? Rating, bool IsSaved,
    Guid? CollectionId, string? MyNote, MapRoute? Route);
public sealed record RouteCollectionDto(Guid Id, string Name, int RoutesCount);
public sealed record RouteReviewDto(Guid Id, UserDto Author, int Rating, string Text, DateOnly RiddenOn, DateTimeOffset UpdatedAt);

public sealed class RouteLibraryService(IDbContextFactory<BikeHubDbContext> contexts, ICurrentUser current,
    PostgresCommunityService community, TimeProvider clock)
{
    private const string MissingTitle = "Маршрут не найден.";
    private const string ForbiddenTitle = "Изменять можно только свой маршрут.";
    private const string ConflictTitle = "Маршрут уже изменился. Обновите страницу.";
    private const string CollectionTitle = "Подборка не найдена.";
    private const string CollectionTakenTitle = "Подборка с таким названием уже есть.";
    private const string CollectionsLimitTitle = "Можно создать до 50 подборок.";
    private const string ReviewDateTitle = "Укажите дату поездки, которая уже состоялась.";
    private const string ReviewHiddenTitle = "Отзыв скрыт модератором.";
    private const string RouteRequiredTitle = "Добавьте маршрут до 100 000 км: GPX или точки на карте.";
    private const string SignInTitle = "Войдите в аккаунт.";
    private const int MaximumCollections = 50;
    private const decimal MaximumDistanceKm = 100000;
    private Guid Actor => current.Id ?? throw new CommunityProblem(401, SignInTitle);

    internal static IQueryable<LibraryRoute> Visible(BikeHubDbContext db) => db.LibraryRoutes.AsNoTracking().Where(r => !r.IsDeleted && !r.IsHidden &&
        db.Accounts.Any(a => a.Id == r.OwnerId && !a.IsBlocked));
    internal static IQueryable<RouteReview> VisibleReviews(BikeHubDbContext db) => db.RouteReviews.AsNoTracking().Where(r => !r.IsDeleted && !r.IsHidden &&
        db.Accounts.Any(a => a.Id == r.AuthorId && !a.IsBlocked));
    private static IQueryable<RouteReview> Reviews(BikeHubDbContext db, Guid id) => VisibleReviews(db).Where(r => r.RouteId == id);
    private LibraryRoute Locked(BikeHubDbContext db, Guid id)
    {
        var route = db.LibraryRoutes.FromSqlInterpolated($"SELECT * FROM library_routes WHERE id = {id} FOR UPDATE").SingleOrDefault();
        if (route is null || !Visible(db).Any(r => r.Id == id))
        {
            throw new CommunityProblem(404, MissingTitle);
        }
        return route;
    }
    private void Manage(LibraryRoute route)
    {
        if (route.OwnerId != Actor && !community.IsModerator)
        {
            throw new CommunityProblem(403, ForbiddenTitle);
        }
    }
    private IReadOnlyList<LibraryRouteDto> Views(BikeHubDbContext db, IQueryable<LibraryRoute> query, bool detail)
    {
        var reviews = VisibleReviews(db);
        var rows = query.Select(r => new { r.Id, r.OwnerId, r.Title, r.Description, r.City, r.Surface, r.DistanceKm, r.Revision, r.CreatedAt,
            ReviewsCount = reviews.Count(v => v.RouteId == r.Id), Rating = reviews.Where(v => v.RouteId == r.Id).Select(v => (double?)v.Rating).Average(),
            IsSaved = db.SavedRoutes.Any(s => s.RouteId == r.Id && s.UserId == current.Id),
            CollectionId = db.SavedRoutes.Where(s => s.RouteId == r.Id && s.UserId == current.Id).Select(s => s.CollectionId).FirstOrDefault(),
            Note = detail ? db.SavedRoutes.Where(s => s.RouteId == r.Id && s.UserId == current.Id).Select(s => s.Note).FirstOrDefault() : null,
            RouteJson = detail ? r.RouteJson : null }).ToArray();
        var authorIds = rows.Select(r => r.OwnerId).Distinct().ToArray();
        var authors = community.Users(db).Where(u => authorIds.Contains(u.Id)).ToDictionary(u => u.Id);
        return rows.Where(r => authors.ContainsKey(r.OwnerId)).Select(r => new LibraryRouteDto(r.Id, authors[r.OwnerId], r.Title, r.Description, r.City, r.Surface,
            r.DistanceKm, r.Revision, r.CreatedAt, r.ReviewsCount, r.Rating, r.IsSaved, r.CollectionId, r.Note, RouteJson.Read(r.RouteJson))).ToArray();
    }
    public LibraryRouteDto Get(Guid id)
    {
        using var db = contexts.CreateDbContext();
        return Views(db, Visible(db).Where(r => r.Id == id), true).SingleOrDefault() ?? throw new CommunityProblem(404, MissingTitle);
    }
    public MapRoute Preview(Guid id)
    {
        using var db = contexts.CreateDbContext();
        var json = Visible(db).Where(r => r.Id == id).Select(r => r.RouteJson).SingleOrDefault() ?? throw new CommunityProblem(404, MissingTitle);
        return RouteService.Preview(RouteJson.Read(json)!);
    }
    public Page<LibraryRouteDto> List(string? city, RouteSurface? surface, decimal? minimum, decimal? maximum, bool mine, bool saved, Guid? collectionId, int page, int size)
    {
        using var db = contexts.CreateDbContext(); page = Math.Clamp(page, 1, 1000000); size = Math.Clamp(size, 1, 50);
        if (minimum < 0 || maximum < 0 || minimum > maximum)
        {
            throw new CommunityProblem(400, RouteRequiredTitle);
        }
        var routes = Visible(db);
        if (mine || saved || collectionId.HasValue)
        {
            var actor = Actor;
            if (mine) { routes = routes.Where(r => r.OwnerId == actor); }
            if (saved || collectionId.HasValue) { routes = routes.Where(r => db.SavedRoutes.Any(s => s.RouteId == r.Id && s.UserId == actor && (!collectionId.HasValue || s.CollectionId == collectionId))); }
            if (collectionId.HasValue && !db.RouteCollections.Any(c => c.Id == collectionId && c.OwnerId == actor))
            {
                throw new CommunityProblem(404, CollectionTitle);
            }
        }
        if (PostgresCommunityService.Search(city) is { } pattern) { routes = routes.Where(r => EF.Functions.ILike(r.City, pattern, Constants.SearchEscapeCharacter)); }
        if (surface.HasValue) { routes = routes.Where(r => r.Surface == surface.Value); }
        if (minimum.HasValue) { routes = routes.Where(r => r.DistanceKm >= minimum); }
        if (maximum.HasValue) { routes = routes.Where(r => r.DistanceKm <= maximum); }
        return new(Views(db, routes.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Skip((page - 1) * size).Take(size), false), routes.Count(), page, size);
    }
    public LibraryRouteDto Save(Guid? id, SaveLibraryRouteRequest request)
    {
        var actor = Actor;
        using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction();
        var now = clock.GetUtcNow();
        var route = id.HasValue ? Locked(db, id.Value) : new LibraryRoute { Id = Guid.CreateVersion7(), OwnerId = actor, CreatedAt = now };
        if (id.HasValue)
        {
            Manage(route);
            if (route.Revision != request.Revision) { throw new CommunityProblem(409, ConflictTitle); }
        }
        var json = RouteService.Save(request.Route) ?? throw new CommunityProblem(400, RouteRequiredTitle);
        var length = RouteService.DistanceKm(request.Route);
        if (length > MaximumDistanceKm) { throw new CommunityProblem(400, RouteRequiredTitle); }
        route.Title = request.Title.Trim(); route.Description = request.Description.Trim(); route.City = request.City.Trim();
        route.Surface = request.Surface; route.RouteJson = json; route.DistanceKm = length; route.Revision++; route.UpdatedAt = now;
        if (!id.HasValue) { db.LibraryRoutes.Add(route); }
        db.SaveChanges(); transaction.Commit(); return Get(route.Id);
    }
    public void Delete(Guid id)
    {
        using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction();
        var route = Locked(db, id); Manage(route); route.IsDeleted = true; route.RouteJson = RouteJson.Write(new MapRoute(string.Empty, [], []))!;
        route.Revision++; db.SaveChanges(); transaction.Commit();
    }
    public LibraryRouteDto Bookmark(Guid id, SaveRouteBookmarkRequest? request)
    {
        var actor = Actor;
        using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction(); _ = Locked(db, id);
        if (request?.CollectionId is { } collectionId && !db.RouteCollections.Any(c => c.Id == collectionId && c.OwnerId == actor))
        {
            throw new CommunityProblem(404, CollectionTitle);
        }
        var saved = db.SavedRoutes.SingleOrDefault(s => s.RouteId == id && s.UserId == actor);
        if (request is null)
        {
            if (saved is not null) { db.SavedRoutes.Remove(saved); }
        }
        else
        {
            if (saved is null) { saved = new SavedRoute { RouteId = id, UserId = actor, CreatedAt = clock.GetUtcNow() }; db.SavedRoutes.Add(saved); }
            saved.CollectionId = request.CollectionId; saved.Note = request.Note?.Trim() ?? string.Empty;
        }
        db.SaveChanges(); transaction.Commit(); return Get(id);
    }
    public IReadOnlyList<RouteCollectionDto> Collections()
    {
        var actor = Actor;
        using var db = contexts.CreateDbContext(); var routes = Visible(db);
        return db.RouteCollections.AsNoTracking().Where(c => c.OwnerId == actor).OrderBy(c => c.NormalizedName).ThenBy(c => c.Id).Take(MaximumCollections)
            .Select(c => new RouteCollectionDto(c.Id, c.Name, db.SavedRoutes.Count(s => s.CollectionId == c.Id && s.UserId == actor && routes.Any(r => r.Id == s.RouteId)))).ToArray();
    }
    public RouteCollectionDto SaveCollection(Guid? id, RouteCollectionRequest request)
    {
        var actor = Actor;
        using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction();
        _ = db.Accounts.FromSqlInterpolated($"SELECT * FROM auth_accounts WHERE id = {actor} FOR UPDATE").Single();
        var name = request.Name.Trim();
        if (name.Length < 2) { throw new CommunityProblem(400, CollectionTitle); }
        var collection = id.HasValue ? db.RouteCollections.SingleOrDefault(c => c.Id == id && c.OwnerId == actor) ?? throw new CommunityProblem(404, CollectionTitle)
            : new RouteCollection { Id = Guid.CreateVersion7(), OwnerId = actor, CreatedAt = clock.GetUtcNow() };
        if (!id.HasValue && db.RouteCollections.Count(c => c.OwnerId == actor) >= MaximumCollections) { throw new CommunityProblem(409, CollectionsLimitTitle); }
        collection.Name = name; collection.NormalizedName = name.ToLowerInvariant();
        if (!id.HasValue) { db.RouteCollections.Add(collection); }
        try { db.SaveChanges(); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new CommunityProblem(409, CollectionTakenTitle);
        }
        transaction.Commit();
        return Collections().Single(c => c.Id == collection.Id);
    }
    public void DeleteCollection(Guid id)
    {
        var actor = Actor; using var db = contexts.CreateDbContext();
        if (db.RouteCollections.Where(c => c.Id == id && c.OwnerId == actor).ExecuteDelete() == 0) { throw new CommunityProblem(404, CollectionTitle); }
    }
    private IQueryable<RouteReviewDto> ReviewViews(BikeHubDbContext db, IQueryable<RouteReview> query) =>
        from r in query join author in community.Users(db) on r.AuthorId equals author.Id
        select new RouteReviewDto(r.Id, author, r.Rating, r.Text, r.RiddenOn, r.UpdatedAt);
    public Page<RouteReviewDto> ReviewList(Guid id, int page, int size)
    {
        using var db = contexts.CreateDbContext();
        if (!Visible(db).Any(r => r.Id == id)) { throw new CommunityProblem(404, MissingTitle); }
        page = Math.Clamp(page, 1, 1000000); size = Math.Clamp(size, 1, 50);
        var query = Reviews(db, id);
        return new(ReviewViews(db, query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Skip((page - 1) * size).Take(size)).ToArray(), query.Count(), page, size);
    }
    public RouteReviewDto Review(Guid id, SaveRouteReviewRequest request)
    {
        var actor = Actor; using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction();
        var route = Locked(db, id); var now = clock.GetUtcNow();
        TimeZoneInfo zone;
        try { zone = request.TimeZone is null ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone); }
        catch (TimeZoneNotFoundException) { throw new CommunityProblem(400, ReviewDateTitle); }
        catch (InvalidTimeZoneException) { throw new CommunityProblem(400, ReviewDateTitle); }
        if (request.RiddenOn > DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime) || request.RiddenOn < new DateOnly(1900, 1, 1)) { throw new CommunityProblem(400, ReviewDateTitle); }
        var review = db.RouteReviews.SingleOrDefault(r => r.RouteId == id && r.AuthorId == actor);
        if (review?.IsHidden == true) { throw new CommunityProblem(403, ReviewHiddenTitle); }
        if (review is null)
        {
            review = new RouteReview { Id = Guid.CreateVersion7(), RouteId = id, AuthorId = actor, CreatedAt = now }; db.RouteReviews.Add(review);
        }
        review.Rating = request.Rating; review.Text = request.Text.Trim(); review.RiddenOn = request.RiddenOn; review.UpdatedAt = now; review.IsDeleted = false;
        db.SaveChanges(); NotificationWriter.Add(db, route.OwnerId, actor, NotificationKind.RouteReview, ReportTarget.RouteReview, review.Id, id, now);
        transaction.Commit(); return ReviewViews(db, Reviews(db, id).Where(r => r.Id == review.Id)).Single();
    }
    public void DeleteReview(Guid id, Guid reviewId)
    {
        var actor = Actor; using var db = contexts.CreateDbContext();
        var review = db.RouteReviews.SingleOrDefault(r => r.RouteId == id && r.Id == reviewId && !r.IsDeleted) ?? throw new CommunityProblem(404, MissingTitle);
        if (review.AuthorId != actor && !community.IsModerator) { throw new CommunityProblem(403, ForbiddenTitle); }
        review.IsDeleted = true; db.SaveChanges();
    }
}
