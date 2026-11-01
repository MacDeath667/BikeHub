using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BikeHub.Dal;

public static class BootstrapperExtensions
{
    public static IHostApplicationBuilder AddBikeHubDatabase(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDbContextFactory<BikeHubDbContext>(options =>
            DatabaseSchema.Configure(options, builder.Configuration.GetConnectionString(Constants.ConnectionStringName).Protect(),
                builder.Configuration[DatabaseSchema.ConfigurationKey] ?? DatabaseSchema.Default));
        builder.Services.AddSingleton<DatabaseMigrator>();
        builder.Services.AddSingleton<DatabaseBootstrapper>();
        return builder;
    }
}
