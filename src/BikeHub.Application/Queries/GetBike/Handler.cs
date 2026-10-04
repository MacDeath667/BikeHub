using BikeHub.Dal;
using Dapper;

namespace BikeHub.Application.Queries.GetBike;

public class Handler(IDbConnectionFactory connectionFactory)
{
    public async Task<BikeDto> Handle(Query query, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();
        var bike = await connection.QueryFirstOrDefaultAsync<Bike>(
            """
            SELECT id, name, description, owner_id as OwnerId
            FROM bikes
            WHERE id = @bikeId
            """,
            new { bikeId = query.BikeId });

        return bike is null
            ? throw new Exception("BikeNotFound")
            : new BikeDto(bike.Id, bike.Name, bike.Description, bike.OwnerId);
    }
}