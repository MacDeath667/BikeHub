namespace BikeHub.Application.Commands.AddBike;

public record Command(string Name, string Description, Guid OwnerId);