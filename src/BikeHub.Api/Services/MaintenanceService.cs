using System.ComponentModel.DataAnnotations;
using BikeHub.Api.Authentication;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Services;

public sealed record SaveBikeComponentRequest([EnumDataType(typeof(BikeComponentKind))] BikeComponentKind Kind,
    [Required, StringLength(100, MinimumLength = 2)] string Name, [StringLength(100)] string Model,
    [StringLength(2000)] string Notes, [Range(0, 1000000)] decimal InstalledMileage, DateOnly InstalledOn,
    [Range(1, 100000)] decimal? ServiceIntervalKm = null, bool IsArchived = false, long? Revision = null,
    [StringLength(100)] string? TimeZone = null);
public sealed record SaveMaintenanceRecordRequest([EnumDataType(typeof(MaintenanceKind))] MaintenanceKind Kind,
    [Required, StringLength(160, MinimumLength = 2)] string Title, [StringLength(4000)] string Notes,
    DateOnly PerformedOn, [Range(0, 1000000)] decimal Mileage, Guid? ComponentId = null,
    [Range(0, 10000000)] decimal? CostRub = null, bool ResetsInterval = false, long? Revision = null,
    [StringLength(100)] string? TimeZone = null);
public sealed record UpdateMileageRequest([Range(0, 1000000)] decimal Mileage);
public sealed record MaintenanceOverviewDto(Guid BikeId, string BikeName, decimal Mileage, int ActiveComponents, int DueComponents);
public sealed record MaintenanceRecordDto(Guid Id, Guid? ComponentId, string? ComponentName, MaintenanceKind Kind, string Title,
    string Notes, DateOnly PerformedOn, decimal Mileage, decimal? CostRub, bool ResetsInterval, long Revision);
public sealed record ComponentOptionDto(Guid Id, string Name, bool IsArchived);

public sealed class MaintenanceService(IDbContextFactory<BikeHubDbContext> contexts, ICurrentUser current, TimeProvider clock)
{
    private const string MissingTitle = "Запись обслуживания не найдена.";
    private const string SignInTitle = "Войдите в аккаунт.";
    private const string ConflictTitle = "Данные уже изменились. Обновите страницу.";
    private const string DateTitle = "Укажите дату уже выполненной работы или установки компонента.";
    private const string MileageTitle = "Пробег установки должен соответствовать одометру и истории обслуживания компонента.";
    private const string ComponentTitle = "Выберите активный компонент этого велосипеда.";
    private const string LimitTitle = "Для велосипеда можно добавить до 200 компонентов, включая архив.";
    private const int MaximumComponents = 200;
    private Guid Actor => current.Id ?? throw new CommunityProblem(401, SignInTitle);
    private Bike Find(BikeHubDbContext db, Guid id) => db.Bikes.AsNoTracking().SingleOrDefault(b => b.Id == id && b.OwnerId == Actor && !b.IsDeleted)
        ?? throw new CommunityProblem(404, MissingTitle);
    private Bike Locked(BikeHubDbContext db, Guid id)
    {
        var actor = Actor;
        // Match account-erasure lock order before locking the owner's bike.
        _ = db.Accounts.FromSqlInterpolated($"SELECT * FROM auth_accounts WHERE id = {actor} FOR KEY SHARE").Single();
        return db.Bikes.FromSqlInterpolated($"SELECT * FROM bikes WHERE id = {id} AND owner_id = {actor} AND NOT is_deleted FOR UPDATE").SingleOrDefault()
            ?? throw new CommunityProblem(404, MissingTitle);
    }
    private static void Revision(long stored, long? requested)
    {
        if (stored != requested) { throw new CommunityProblem(409, ConflictTitle); }
    }
    private static IQueryable<MaintenanceRecordDto> Records(BikeHubDbContext db, Guid bikeId, Guid recordId) => db.MaintenanceRecords.AsNoTracking()
        .Where(r => r.BikeId == bikeId && r.Id == recordId && !r.IsDeleted)
        .Select(r => new MaintenanceRecordDto(r.Id, r.ComponentId, db.BikeComponents.Where(c => c.Id == r.ComponentId).Select(c => c.Name).FirstOrDefault(),
            r.Kind, r.Title, r.Notes, r.PerformedOn, r.Mileage, r.CostRub, r.ResetsInterval, r.Revision));
    public MaintenanceOverviewDto Overview(Guid id)
    {
        using var db = contexts.CreateDbContext(); var bike = Find(db, id);
        var components = MaintenanceQueries.Components(db).Where(c => c.BikeId == id && !c.IsArchived);
        return new(id, bike.Name, bike.Mileage, components.Count(), components.Count(c => c.DueMileage.HasValue && c.CurrentMileage >= c.DueMileage));
    }
    public Page<BikeComponentDto> Components(Guid bikeId, bool archived, int page, int size)
    {
        using var db = contexts.CreateDbContext(); _ = Find(db, bikeId);
        page = Math.Clamp(page, 1, 1000000); size = Math.Clamp(size, 1, 50);
        var query = MaintenanceQueries.Components(db).Where(c => c.BikeId == bikeId && c.IsArchived == archived);
        return new(query.OrderBy(c => c.Name).ThenBy(c => c.Id).Skip((page - 1) * size).Take(size).ToArray(), query.Count(), page, size);
    }
    public IReadOnlyList<ComponentOptionDto> ComponentOptions(Guid bikeId)
    {
        using var db = contexts.CreateDbContext(); _ = Find(db, bikeId);
        return db.BikeComponents.AsNoTracking().Where(c => c.BikeId == bikeId).OrderBy(c => c.Name).ThenBy(c => c.Id).Take(MaximumComponents)
            .Select(c => new ComponentOptionDto(c.Id, c.Name, c.IsArchived)).ToArray();
    }
    public BikeComponentDto SaveComponent(Guid bikeId, Guid? id, SaveBikeComponentRequest request)
    {
        using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction(); var bike = Locked(db, bikeId);
        CalendarDates.ValidatePast(request.InstalledOn, request.TimeZone, clock.GetUtcNow(), DateTitle);
        var component = id.HasValue ? db.BikeComponents.SingleOrDefault(c => c.Id == id && c.BikeId == bikeId) ?? throw new CommunityProblem(404, MissingTitle)
            : new BikeComponent { Id = Guid.CreateVersion7(), BikeId = bikeId, CreatedAt = clock.GetUtcNow() };
        if (id.HasValue) { Revision(component.Revision, request.Revision); }
        else if (db.BikeComponents.Count(c => c.BikeId == bikeId) >= MaximumComponents) { throw new CommunityProblem(409, LimitTitle); }
        if (request.InstalledMileage > bike.Mileage || db.MaintenanceRecords.Any(r => r.ComponentId == component.Id && !r.IsDeleted &&
                (r.Mileage < request.InstalledMileage || r.PerformedOn < request.InstalledOn)))
        {
            throw new CommunityProblem(400, MileageTitle);
        }
        component.Kind = request.Kind; component.Name = request.Name.Trim(); component.Model = request.Model?.Trim() ?? string.Empty;
        component.Notes = request.Notes?.Trim() ?? string.Empty; component.InstalledMileage = request.InstalledMileage; component.InstalledOn = request.InstalledOn;
        component.ServiceIntervalKm = request.ServiceIntervalKm; component.IsArchived = request.IsArchived; component.Revision++; component.UpdatedAt = clock.GetUtcNow();
        if (!id.HasValue) { db.BikeComponents.Add(component); }
        db.SaveChanges(); MaintenanceQueries.RefreshReminders(db, bikeId, clock.GetUtcNow()); transaction.Commit();
        return MaintenanceQueries.Components(db).Single(c => c.Id == component.Id);
    }
    public Page<MaintenanceRecordDto> History(Guid bikeId, Guid? componentId, int page, int size)
    {
        using var db = contexts.CreateDbContext(); _ = Find(db, bikeId);
        page = Math.Clamp(page, 1, 1000000); size = Math.Clamp(size, 1, 50);
        var query = db.MaintenanceRecords.AsNoTracking().Where(r => r.BikeId == bikeId && !r.IsDeleted && (!componentId.HasValue || r.ComponentId == componentId));
        // Keep filtering and ordering before the DTO projection.
        var rows = query.OrderByDescending(r => r.PerformedOn).ThenByDescending(r => r.Id).Skip((page - 1) * size).Take(size);
        var items = from r in rows
                    select new MaintenanceRecordDto(r.Id, r.ComponentId, db.BikeComponents.Where(c => c.Id == r.ComponentId).Select(c => c.Name).FirstOrDefault(),
                        r.Kind, r.Title, r.Notes, r.PerformedOn, r.Mileage, r.CostRub, r.ResetsInterval, r.Revision);
        return new(items.ToArray(), query.Count(), page, size);
    }
    public MaintenanceRecordDto SaveRecord(Guid bikeId, Guid? id, SaveMaintenanceRecordRequest request)
    {
        using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction(); var bike = Locked(db, bikeId);
        CalendarDates.ValidatePast(request.PerformedOn, request.TimeZone, clock.GetUtcNow(), DateTitle);
        var record = id.HasValue ? db.MaintenanceRecords.SingleOrDefault(r => r.Id == id && r.BikeId == bikeId && !r.IsDeleted) ?? throw new CommunityProblem(404, MissingTitle)
            : new MaintenanceRecord { Id = Guid.CreateVersion7(), BikeId = bikeId, CreatedAt = clock.GetUtcNow() };
        if (id.HasValue) { Revision(record.Revision, request.Revision); }
        if (request.ComponentId.HasValue)
        {
            var component = db.BikeComponents.SingleOrDefault(c => c.Id == request.ComponentId && c.BikeId == bikeId && (!c.IsArchived || c.Id == record.ComponentId)) ?? throw new CommunityProblem(400, ComponentTitle);
            if (request.Mileage < component.InstalledMileage || request.PerformedOn < component.InstalledOn) { throw new CommunityProblem(400, MileageTitle); }
        }
        else if (request.ResetsInterval) { throw new CommunityProblem(400, ComponentTitle); }
        record.ComponentId = request.ComponentId; record.Kind = request.Kind; record.Title = request.Title.Trim(); record.Notes = request.Notes?.Trim() ?? string.Empty;
        record.PerformedOn = request.PerformedOn; record.Mileage = request.Mileage; record.CostRub = request.CostRub; record.ResetsInterval = request.ResetsInterval;
        record.Revision++; record.UpdatedAt = clock.GetUtcNow(); bike.Mileage = Math.Max(bike.Mileage, request.Mileage);
        if (!id.HasValue) { db.MaintenanceRecords.Add(record); }
        db.SaveChanges(); MaintenanceQueries.RefreshReminders(db, bikeId, clock.GetUtcNow()); transaction.Commit();
        return Records(db, bikeId, record.Id).Single();
    }
    public void DeleteRecord(Guid bikeId, Guid id)
    {
        using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction(); _ = Locked(db, bikeId);
        var record = db.MaintenanceRecords.SingleOrDefault(r => r.Id == id && r.BikeId == bikeId && !r.IsDeleted) ?? throw new CommunityProblem(404, MissingTitle);
        record.IsDeleted = true; record.Revision++; db.SaveChanges(); MaintenanceQueries.RefreshReminders(db, bikeId, clock.GetUtcNow()); transaction.Commit();
    }
    public MaintenanceOverviewDto Mileage(Guid bikeId, decimal mileage)
    {
        using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction(); var bike = Locked(db, bikeId);
        bike.Mileage = mileage; db.SaveChanges(); MaintenanceQueries.RefreshReminders(db, bikeId, clock.GetUtcNow()); transaction.Commit(); return Overview(bikeId);
    }
}
