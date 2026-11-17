namespace BikeHub.Dal.Entities;

public enum RideStatus { Scheduled = 1, Cancelled = 2 }
public enum RideAttendance { Going = 1, Maybe = 2 }
public enum RideDifficulty { Easy = 1, Moderate = 2, Hard = 3 }

public sealed class GroupRide
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid? GroupId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string MeetingName { get; set; } = string.Empty;
    public double? MeetingLat { get; set; }
    public double? MeetingLng { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public string TimeZone { get; set; } = string.Empty;
    public decimal DistanceKm { get; set; }
    public decimal? AverageSpeedKmh { get; set; }
    public RideDifficulty Difficulty { get; set; }
    public int? Capacity { get; set; }
    public string? RouteJson { get; set; }
    public RideStatus Status { get; set; }
    public long Revision { get; set; }
    public bool IsHidden { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class RideParticipant
{
    public Guid RideId { get; set; }
    public Guid UserId { get; set; }
    public RideAttendance Attendance { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RideChange
{
    public Guid Id { get; set; }
    public Guid RideId { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
