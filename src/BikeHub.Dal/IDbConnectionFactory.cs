using System.Data;

namespace BikeHub.Dal;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}