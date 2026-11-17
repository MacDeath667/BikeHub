namespace BikeHub.Dal.Entities;

public enum RouteSurface { Asphalt = 1, Gravel = 2, Trail = 3, Mixed = 4 }

public sealed class LibraryRoute
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public RouteSurface Surface { get; set; }
    public decimal DistanceKm { get; set; }
    public string RouteJson { get; set; } = string.Empty;
    public long Revision { get; set; }
    public bool IsHidden { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class RouteCollection
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SavedRoute
{
    public Guid RouteId { get; set; }
    public Guid UserId { get; set; }
    public Guid? CollectionId { get; set; }
    public string Note { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RouteReview
{
    public Guid Id { get; set; }
    public Guid RouteId { get; set; }
    public Guid AuthorId { get; set; }
    public int Rating { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateOnly RiddenOn { get; set; }
    public bool IsHidden { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
