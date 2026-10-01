namespace BikeHub.Application.Queries.GetBike;

public class Query(Guid bikeId)
{
    public Guid BikeId => bikeId;
}