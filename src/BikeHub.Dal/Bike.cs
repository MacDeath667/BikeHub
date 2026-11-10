namespace BikeHub.Dal;

public sealed class Bike
{
    public string Brand { get; set; } = string.Empty;
    public int Category { get; set; } = 3;
    public int Year { get; set; }
    public decimal Mileage { get; set; }
    public Guid? ImageId { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsHidden { get; set; }
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
