using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BikeHub.Dal;

public static class BootstrapperExtensions
{
    public static IHostApplicationBuilder AddDapper(this IHostApplicationBuilder builder, string connectionName)
    {
        var connectionString = builder.Configuration.GetConnectionString(connectionName).Protect();

        builder.Services.AddSingleton<IDbConnectionFactory>(new SqlDbConnectionFactory(connectionString));
        return builder;
    }
}

public static class ConfigExtensions
{
    public static T Protect<T>(this T? obj) where T : class
    {
        return obj ?? throw new NullReferenceException("Was null");
    }
}
