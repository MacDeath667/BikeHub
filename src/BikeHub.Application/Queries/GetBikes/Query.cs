namespace BikeHub.Application.Queries.GetBikes;

public class Query(Guid ownerId)
{
    public Guid OwnerId => ownerId;
}