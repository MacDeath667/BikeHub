namespace BikeHub.Application.Queries.GetBike;

public class Query(Guid bikeId)
{
    public Guid BikeId => bikeId;
}

public class Handler
{
    public async Task<BikeDto> Handle(Query query, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        return new BikeDto(query.BikeId, "Merida", "Classic road bike", Guid.NewGuid());
    }
}

public record BikeDto(Guid Id, string Name, string Description, Guid OwnerId);