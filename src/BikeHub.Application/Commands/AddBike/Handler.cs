using BikeHub.Dal;
using Dapper;

namespace BikeHub.Application.Commands.AddBike;

public class Handler(IDbConnectionFactory connectionFactory)
{
    private const string InsertBikeSql = """
        INSERT into bikes (name, description, owner_id)
        values (@Name, @Description, @OwnerId)
        """;

    public async Task Handle(Command command, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();
        var inserts = await connection.ExecuteAsync(
            InsertBikeSql,
            new
            {
                @Name = command.Name,
                @Description = command.Description,
                @OwnerId = command.OwnerId
            });

    }
}