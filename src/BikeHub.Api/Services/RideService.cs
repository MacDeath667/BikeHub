using System.ComponentModel.DataAnnotations;
using BikeHub.Api.Authentication;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Services;

public sealed record SaveRideRequest(
    [Required, StringLength(160, MinimumLength = 3)] string Title,
    [Required, StringLength(10000)] string Description,
    [Required, StringLength(100)] string City,
    [Required, StringLength(300)] string MeetingName,
    DateTimeOffset StartsAt, [Required, StringLength(100)] string TimeZone,
    [Range(0, 10000)] decimal DistanceKm,
    [Range(1, 100)] decimal? AverageSpeedKmh,
    [EnumDataType(typeof(RideDifficulty))] RideDifficulty Difficulty,
    [Range(1, 10000)] int? Capacity = null,
    Guid? GroupId = null, GeoPoint? MeetingPoint = null, MapRoute? Route = null, long? Revision = null);
public sealed record RideRevisionRequest([Range(1, long.MaxValue)] long Revision);
public sealed record RideAttendanceRequest([EnumDataType(typeof(RideAttendance))] RideAttendance Attendance);
public sealed record RideDto(Guid Id, UserDto Organizer, Guid? GroupId, string? GroupName, string Title, string Description,
    string City, string MeetingName, GeoPoint? MeetingPoint, DateTimeOffset StartsAt, string TimeZone,
    decimal DistanceKm, decimal? AverageSpeedKmh, RideDifficulty Difficulty, int? Capacity,
    RideStatus Status, long Revision, int Going, int Maybe, RideAttendance? Attendance, bool HasRoute, MapRoute? Route);
public sealed record RideParticipantDto(UserDto User, RideAttendance Attendance);

public sealed class RideService(IDbContextFactory<BikeHubDbContext> contexts, ICurrentUser current,
    PostgresCommunityService community, TimeProvider clock)
{
    private const string MissingTitle = "Покатушка не найдена.";
    private const string ForbiddenTitle = "Изменять покатушку может организатор.";
    private const string ConflictTitle = "Покатушка уже изменилась. Обновите страницу.";
    private const string FullTitle = "Свободных мест больше нет. Можно выбрать «Возможно».";
    private const string ClosedTitle = "Запись на эту покатушку закрыта.";
    private const string OwnerTitle = "Организатор участвует в покатушке. Для отмены встречи используйте отмену покатушки.";
    private const string InvalidDateTitle = "Укажите время начала в будущем и корректный часовой пояс.";
    private const string InvalidPointTitle = "Укажите корректные координаты места встречи.";
    private const string MembershipTitle = "Для покатушки сообщества нужно состоять в нём.";
    private const string CapacityTitle = "Число мест не может быть меньше числа записавшихся участников.";
    private const string SignInTitle = "Войдите в аккаунт.";
    private const string CancelFirstTitle = "Сначала отмените покатушку, чтобы уведомить участников.";
    private Guid Actor => current.Id ?? throw new CommunityProblem(401, SignInTitle);

    internal static IQueryable<GroupRide> Visible(BikeHubDbContext db) => db.Rides.AsNoTracking().Where(r => !r.IsDeleted && !r.IsHidden &&
        db.Accounts.Any(a => a.Id == r.OwnerId && !a.IsBlocked) &&
        (!r.GroupId.HasValue || db.Communities.Any(g => g.Id == r.GroupId && !g.IsDeleted && !g.IsHidden)));
    private static IQueryable<RideParticipant> Participants(BikeHubDbContext db, Guid id) => db.RideParticipants.AsNoTracking()
        .Where(p => p.RideId == id && db.Accounts.Any(a => a.Id == p.UserId && !a.IsBlocked));
    private void Manage(GroupRide ride)
    {
        if (ride.OwnerId != Actor && !community.IsModerator)
        {
            throw new CommunityProblem(403, ForbiddenTitle);
        }
    }
    private static void Revision(GroupRide ride, long? revision)
    {
        if (ride.Revision != revision)
        {
            throw new CommunityProblem(409, ConflictTitle);
        }
    }
    private GroupRide Locked(BikeHubDbContext db, Guid id)
    {
        var ride = db.Rides.FromSqlInterpolated($"SELECT * FROM rides WHERE id = {id} FOR UPDATE").SingleOrDefault();
        if (ride is null || !Visible(db).Any(r => r.Id == id))
        {
            throw new CommunityProblem(404, MissingTitle);
        }
        return ride;
    }
    private IReadOnlyList<RideDto> Views(BikeHubDbContext db, IQueryable<GroupRide> query, bool detail)
    {
        var rows = query.Select(r => new { r.Id, r.OwnerId, r.GroupId, r.Title, r.Description, r.City, r.MeetingName, r.MeetingLat, r.MeetingLng,
            r.StartsAt, r.TimeZone, r.DistanceKm, r.AverageSpeedKmh, r.Difficulty, r.Capacity, r.Status, r.Revision,
            GroupName = db.Communities.Where(g => g.Id == r.GroupId).Select(g => g.Name).FirstOrDefault(),
            Going = db.RideParticipants.Count(p => p.RideId == r.Id && p.Attendance == RideAttendance.Going && db.Accounts.Any(a => a.Id == p.UserId && !a.IsBlocked)),
            Maybe = db.RideParticipants.Count(p => p.RideId == r.Id && p.Attendance == RideAttendance.Maybe && db.Accounts.Any(a => a.Id == p.UserId && !a.IsBlocked)),
            Attendance = db.RideParticipants.Where(p => p.RideId == r.Id && p.UserId == current.Id).Select(p => (RideAttendance?)p.Attendance).FirstOrDefault(),
            HasRoute = r.RouteJson != null, RouteJson = detail ? r.RouteJson : null }).ToArray();
        var owners = rows.Select(r => r.OwnerId).Distinct().ToArray();
        var authors = community.Users(db).Where(u => owners.Contains(u.Id)).ToDictionary(u => u.Id);
        return rows.Where(r => authors.ContainsKey(r.OwnerId)).Select(r => new RideDto(r.Id, authors[r.OwnerId], r.GroupId, r.GroupName,
            r.Title, r.Description, r.City, r.MeetingName, r.MeetingLat.HasValue ? new GeoPoint(r.MeetingLat.Value, r.MeetingLng!.Value) : null,
            r.StartsAt, r.TimeZone, r.DistanceKm, r.AverageSpeedKmh, r.Difficulty, r.Capacity, r.Status, r.Revision,
            r.Going, r.Maybe, r.Attendance, r.HasRoute, RouteJson.Read(r.RouteJson))).ToArray();
    }
    public RideDto Get(Guid id)
    {
        using var db = contexts.CreateDbContext();
        return Views(db, Visible(db).Where(r => r.Id == id), true).SingleOrDefault() ?? throw new CommunityProblem(404, MissingTitle);
    }
    public Page<RideDto> List(string? city, Guid? groupId, bool mine, bool past, int page, int size)
    {
        using var db = contexts.CreateDbContext();
        page = Math.Clamp(page, 1, 1000000); size = Math.Clamp(size, 1, 50);
        var query = Visible(db);
        if (mine)
        {
            var actor = Actor;
            query = query.Where(r => r.OwnerId == actor || db.RideParticipants.Any(p => p.RideId == r.Id && p.UserId == actor));
        }
        if (groupId.HasValue)
        {
            query = query.Where(r => r.GroupId == groupId);
        }
        if (PostgresCommunityService.Search(city) is { } pattern)
        {
            query = query.Where(r => EF.Functions.ILike(r.City, pattern, Constants.SearchEscapeCharacter));
        }
        var now = clock.GetUtcNow();
        query = past ? query.Where(r => r.StartsAt <= now) : query.Where(r => r.StartsAt > now && (mine || r.Status == RideStatus.Scheduled));
        var ordered = past ? query.OrderByDescending(r => r.StartsAt).ThenBy(r => r.Id) : query.OrderBy(r => r.StartsAt).ThenBy(r => r.Id);
        return new(Views(db, ordered.Skip((page - 1) * size).Take(size), false), query.Count(), page, size);
    }
    public Page<RideParticipantDto> Members(Guid id, int page, int size)
    {
        using var db = contexts.CreateDbContext();
        if (!Visible(db).Any(r => r.Id == id))
        {
            throw new CommunityProblem(404, MissingTitle);
        }
        page = Math.Clamp(page, 1, 1000000); size = Math.Clamp(size, 1, 50);
        var query = Participants(db, id);
        var members = from p in query
                      join u in community.Users(db) on p.UserId equals u.Id
                      orderby p.Attendance, p.CreatedAt, p.UserId
                      select new RideParticipantDto(u, p.Attendance);
        return new(members.Skip((page - 1) * size).Take(size).ToArray(), query.Count(), page, size);
    }
    public RideDto Save(Guid? id, SaveRideRequest request)
    {
        var actor = Actor;
        using var db = contexts.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();
        var now = clock.GetUtcNow();
        // PostgreSQL timestamps store microseconds; normalize before comparing a repeated request.
        var startsAt = request.StartsAt.ToUniversalTime();
        startsAt = startsAt.AddTicks(-(startsAt.Ticks % TimeSpan.TicksPerMicrosecond));
        var ride = id.HasValue ? Locked(db, id.Value) : new GroupRide { Id = Guid.CreateVersion7(), OwnerId = actor, CreatedAt = now, Status = RideStatus.Scheduled };
        if (id.HasValue)
        {
            Manage(ride); Revision(ride, request.Revision);
        }
        if (startsAt <= now || startsAt > now.AddYears(2))
        {
            throw new CommunityProblem(400, InvalidDateTitle);
        }
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone); }
        catch (TimeZoneNotFoundException) { throw new CommunityProblem(400, InvalidDateTitle); }
        catch (InvalidTimeZoneException) { throw new CommunityProblem(400, InvalidDateTitle); }
        if (request.MeetingPoint is { } point && (!double.IsFinite(point.Lat) || !double.IsFinite(point.Lng) || point.Lat is < -90 or > 90 || point.Lng is < -180 or > 180))
        {
            throw new CommunityProblem(400, InvalidPointTitle);
        }
        if (request.GroupId.HasValue && !db.Relations.Any(r => r.UserId == actor && r.TargetId == request.GroupId && r.Kind == RelationKind.Membership &&
                db.Communities.Any(g => g.Id == r.TargetId && !g.IsDeleted && !g.IsHidden)))
        {
            throw new CommunityProblem(403, MembershipTitle);
        }
        if (request.Capacity.HasValue && request.Capacity.Value < (id.HasValue ? Participants(db, ride.Id).Count(p => p.Attendance == RideAttendance.Going) : 1))
        {
            throw new CommunityProblem(409, CapacityTitle);
        }
        var route = RouteService.Save(request.Route);
        var unchanged = ride.Title == request.Title.Trim() && ride.Description == request.Description.Trim() && ride.City == request.City.Trim() &&
            ride.MeetingName == request.MeetingName.Trim() && ride.MeetingLat == request.MeetingPoint?.Lat && ride.MeetingLng == request.MeetingPoint?.Lng &&
            ride.StartsAt == startsAt && ride.TimeZone == request.TimeZone && ride.DistanceKm == request.DistanceKm &&
            ride.AverageSpeedKmh == request.AverageSpeedKmh && ride.Difficulty == request.Difficulty && ride.Capacity == request.Capacity &&
            ride.GroupId == request.GroupId && RouteJson.Write(RouteJson.Read(ride.RouteJson)) == route;
        if (id.HasValue && unchanged)
        {
            transaction.Commit(); return Get(ride.Id);
        }
        ride.Title = request.Title.Trim(); ride.Description = request.Description.Trim(); ride.City = request.City.Trim(); ride.MeetingName = request.MeetingName.Trim();
        ride.MeetingLat = request.MeetingPoint?.Lat; ride.MeetingLng = request.MeetingPoint?.Lng;
        ride.StartsAt = startsAt; ride.TimeZone = request.TimeZone;
        ride.DistanceKm = request.DistanceKm; ride.AverageSpeedKmh = request.AverageSpeedKmh; ride.Difficulty = request.Difficulty;
        ride.Capacity = request.Capacity; ride.GroupId = request.GroupId; ride.RouteJson = route; ride.Revision++; ride.UpdatedAt = now;
        if (!id.HasValue)
        {
            db.Rides.Add(ride);
            db.RideParticipants.Add(new RideParticipant { RideId = ride.Id, UserId = actor, Attendance = RideAttendance.Going, CreatedAt = now });
        }
        db.SaveChanges();
        if (id.HasValue)
        {
            Notify(db, ride, NotificationKind.RideChanged);
        }
        transaction.Commit(); return Get(ride.Id);
    }
    private void Notify(BikeHubDbContext db, GroupRide ride, NotificationKind kind)
    {
        var change = new RideChange { Id = Guid.CreateVersion7(), RideId = ride.Id, Revision = ride.Revision, CreatedAt = clock.GetUtcNow() };
        db.RideChanges.Add(change); db.SaveChanges();
        NotificationWriter.RideChanged(db, change, Actor, kind);
    }
    public RideDto Cancel(Guid id, long revision)
    {
        using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction();
        var ride = Locked(db, id); Manage(ride);
        if (ride.Status != RideStatus.Cancelled)
        {
            Revision(ride, revision); ride.Status = RideStatus.Cancelled; ride.Revision++; ride.UpdatedAt = clock.GetUtcNow(); db.SaveChanges();
            Notify(db, ride, NotificationKind.RideCancelled);
        }
        transaction.Commit(); return Get(id);
    }
    public RideDto Attend(Guid id, RideAttendance? attendance)
    {
        var actor = Actor;
        using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction();
        var ride = Locked(db, id);
        var participant = db.RideParticipants.SingleOrDefault(p => p.RideId == id && p.UserId == actor);
        if (ride.OwnerId == actor && attendance != RideAttendance.Going)
        {
            throw new CommunityProblem(409, OwnerTitle);
        }
        if (attendance.HasValue && (ride.Status != RideStatus.Scheduled || ride.StartsAt <= clock.GetUtcNow()))
        {
            throw new CommunityProblem(409, ClosedTitle);
        }
        if (attendance == RideAttendance.Going && participant?.Attendance != RideAttendance.Going && ride.Capacity.HasValue &&
            Participants(db, id).Count(p => p.Attendance == RideAttendance.Going) >= ride.Capacity.Value)
        {
            throw new CommunityProblem(409, FullTitle);
        }
        if (!attendance.HasValue)
        {
            if (participant is not null) { db.RideParticipants.Remove(participant); }
        }
        else if (participant is null)
        {
            db.RideParticipants.Add(new RideParticipant { RideId = id, UserId = actor, Attendance = attendance.Value, CreatedAt = clock.GetUtcNow() });
        }
        else
        {
            participant.Attendance = attendance.Value;
        }
        db.SaveChanges(); transaction.Commit(); return Get(id);
    }
    public void Delete(Guid id)
    {
        using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction();
        var ride = Locked(db, id); Manage(ride);
        if (ride.Status != RideStatus.Cancelled && Participants(db, id).Any(p => p.UserId != ride.OwnerId))
        {
            throw new CommunityProblem(409, CancelFirstTitle);
        }
        ride.IsDeleted = true; ride.RouteJson = null; ride.Revision++; db.SaveChanges(); transaction.Commit();
    }
}
