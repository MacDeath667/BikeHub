using BikeHub.Dal;
using Dapper;

namespace BikeHub.Application.Queries.GetBike;

public class Query(Guid bikeId)
{
    public Guid BikeId => bikeId;
}

public class Handler(IDbConnectionFactory connectionFactory)
{
    public async Task<BikeDto> Handle(Query query, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();
        var bike = await connection
            .QueryFirstAsync<Bike>("SELECT Id, Name, Description, OwnerId FROM Bike WHERE Id = @bikeId", query.BikeId);

        // await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
         return new BikeDto(bike.Id, bike.Name, bike.Description, bike.OwnerId);
    }
}

public record BikeDto(Guid Id, string Name, string Description, Guid OwnerId);