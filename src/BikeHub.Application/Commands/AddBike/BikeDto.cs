namespace BikeHub.Application.Commands.AddBike;

public record BikeDto(Guid Id, string Name, string Description, Guid OwnerId);