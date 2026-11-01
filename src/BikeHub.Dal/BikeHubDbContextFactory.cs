using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BikeHub.Dal;

public sealed class BikeHubDbContextFactory : IDesignTimeDbContextFactory<BikeHubDbContext>
{
    private const string ConnectionStringVariable = "ConnectionStrings__BikeHub";
    private const string DesignTimeConnectionString = "Host=localhost;Database=bikehub;Username=bikehub";

    public BikeHubDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable) ?? DesignTimeConnectionString;
        var options = new DbContextOptionsBuilder<BikeHubDbContext>();
        DatabaseSchema.Configure(options, connectionString,
            Environment.GetEnvironmentVariable("Database__Schema") ?? DatabaseSchema.Default);
        return new(options.Options);
    }
}
