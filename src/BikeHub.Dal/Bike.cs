namespace BikeHub.Dal;

public record Bike (Guid Id, Guid OwnerId, string Name, string Description);