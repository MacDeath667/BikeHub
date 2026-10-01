namespace BikeHub.Application.Queries.GetBikes;

public record BikeDto(Guid Id, string Name, string Description, Guid OwnerId);