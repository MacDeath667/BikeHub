namespace BikeHub.Application.Queries.GetBikes;

public class Handler
{
    public async Task<IReadOnlyCollection<BikeDto>> Handle(Query query, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        return
        [
            new BikeDto(Guid.NewGuid(), "Merida Scultura", "Classic road bike", query.OwnerId),
            new BikeDto(Guid.NewGuid(), "Mongoose Tyax", "MTB", query.OwnerId)
        ];
    }
}