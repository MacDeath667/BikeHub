namespace BikeHub.Application.Queries.GetBike;

public record BikeDto(Guid Id, string Name, string Description, Guid OwnerId);