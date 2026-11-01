using BikeHub.Dal;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Application.Queries.GetBike;

public class Handler(IDbContextFactory<BikeHubDbContext> contexts)
{
    private const string BikeNotFoundMessage = "BikeNotFound";

    public async Task<BikeDto> Handle(Query query, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var bike = await context.Bikes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == query.BikeId, cancellationToken);
        return bike is null
            ? throw new Exception(BikeNotFoundMessage)
            : new BikeDto(bike.Id, bike.Name, bike.Description ?? string.Empty, bike.OwnerId);
    }
}
