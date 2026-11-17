using System.Text.Json.Serialization;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Services;

public sealed record BikeComponentDto
{
    public Guid Id { get; init; }
    public Guid BikeId { get; init; }
    public BikeComponentKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public decimal InstalledMileage { get; init; }
    public DateOnly InstalledOn { get; init; }
    public decimal? ServiceIntervalKm { get; init; }
    public decimal LastServiceMileage { get; init; }
    public decimal? DueMileage { get; init; }
    public decimal CurrentMileage { get; init; }
    public bool IsArchived { get; init; }
    public long Revision { get; init; }
    public bool IsDue => !IsArchived && DueMileage.HasValue && CurrentMileage >= DueMileage;
    [JsonIgnore] public Guid OwnerId { get; init; }
    [JsonIgnore] public bool BikeDeleted { get; init; }
}

internal static class MaintenanceQueries
{
    internal static IQueryable<BikeComponentDto> Components(BikeHubDbContext db) =>
        from c in db.BikeComponents.AsNoTracking()
        join bike in db.Bikes.AsNoTracking() on c.BikeId equals bike.Id
        let lastService = db.MaintenanceRecords.Where(r => r.ComponentId == c.Id && r.ResetsInterval && !r.IsDeleted).Select(r => (decimal?)r.Mileage).Max() ?? c.InstalledMileage
        select new BikeComponentDto
        {
            Id = c.Id, BikeId = c.BikeId, OwnerId = bike.OwnerId, BikeDeleted = bike.IsDeleted, Kind = c.Kind,
            Name = c.Name, Model = c.Model, Notes = c.Notes, InstalledMileage = c.InstalledMileage, InstalledOn = c.InstalledOn,
            ServiceIntervalKm = c.ServiceIntervalKm, LastServiceMileage = lastService,
            DueMileage = c.ServiceIntervalKm.HasValue ? lastService + c.ServiceIntervalKm.Value : null,
            CurrentMileage = bike.Mileage, IsArchived = c.IsArchived, Revision = c.Revision
        };

    // Caller holds the bike row lock, inside its mutation transaction.
    internal static void RefreshReminders(BikeHubDbContext db, Guid bikeId, DateTimeOffset now)
    {
        db.Database.ExecuteSqlInterpolated($"""
            WITH due AS (
                SELECT c.id AS component_id, b.id AS bike_id, b.owner_id, b.mileage,
                       COALESCE((SELECT max(r.mileage) FROM maintenance_records r
                                  WHERE r.component_id = c.id AND r.resets_interval AND NOT r.is_deleted), c.installed_mileage) + c.service_interval_km AS due_mileage
                  FROM bike_components c JOIN bikes b ON b.id = c.bike_id
                 WHERE b.id = {bikeId} AND NOT b.is_deleted AND NOT c.is_archived AND c.service_interval_km IS NOT NULL
            ), events AS (
                INSERT INTO maintenance_reminders (id, component_id, due_mileage, created_at)
                SELECT uuidv7(), component_id, due_mileage, {now} FROM due WHERE mileage >= due_mileage
                ON CONFLICT (component_id, due_mileage) DO NOTHING
                RETURNING id, component_id
            )
            INSERT INTO notifications (id, recipient_id, actor_id, kind, target_type, target_id, container_id, created_at)
            SELECT uuidv7(), d.owner_id, d.owner_id, {(int)NotificationKind.MaintenanceDue}, {(int)ReportTarget.MaintenanceReminder}, e.id, d.bike_id, {now}
              FROM events e JOIN due d ON d.component_id = e.component_id
            ON CONFLICT (recipient_id, actor_id, kind, target_type, target_id) DO NOTHING
            """);
    }
}
