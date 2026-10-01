namespace BikeHub.Dal;

using System.Data;
using Npgsql;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}

public class SqlDbConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public IDbConnection CreateConnection()
    {
        return new NpgsqlConnection(connectionString);
    }
}