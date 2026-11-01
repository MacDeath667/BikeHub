using BikeHub.Dal;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Application.Commands.AddBike;

public class Handler(IDbContextFactory<BikeHubDbContext> contexts)
{
    public async Task Handle(Command command, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        context.Bikes.Add(new Bike
        {
            Id = Guid.CreateVersion7(),
            OwnerId = command.OwnerId,
            Name = command.Name,
            Description = command.Description
        });
        await context.SaveChangesAsync(cancellationToken);
    }
}
