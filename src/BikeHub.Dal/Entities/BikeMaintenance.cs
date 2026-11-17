namespace BikeHub.Dal.Entities;

public enum BikeComponentKind { Chain = 1, Cassette = 2, Tires = 3, Brakes = 4, Drivetrain = 5, Fork = 6, Shock = 7, Wheels = 8, Other = 9 }
public enum MaintenanceKind { Service = 1, Replacement = 2, Inspection = 3, Lubrication = 4, Repair = 5, Other = 6 }

public sealed class BikeComponent
{
    public Guid Id { get; set; }
    public Guid BikeId { get; set; }
    public BikeComponentKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public decimal InstalledMileage { get; set; }
    public DateOnly InstalledOn { get; set; }
    public decimal? ServiceIntervalKm { get; set; }
    public bool IsArchived { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class MaintenanceRecord
{
    public Guid Id { get; set; }
    public Guid BikeId { get; set; }
    public Guid? ComponentId { get; set; }
    public MaintenanceKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateOnly PerformedOn { get; set; }
    public decimal Mileage { get; set; }
    public decimal? CostRub { get; set; }
    public bool ResetsInterval { get; set; }
    public bool IsDeleted { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class MaintenanceReminder
{
    public Guid Id { get; set; }
    public Guid ComponentId { get; set; }
    public decimal DueMileage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
