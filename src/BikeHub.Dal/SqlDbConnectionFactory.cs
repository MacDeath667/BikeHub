namespace BikeHub.Dal;

using System.Data;
using Npgsql;

public class SqlDbConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public IDbConnection CreateConnection()
    {
        return new NpgsqlConnection(connectionString);
    }
}