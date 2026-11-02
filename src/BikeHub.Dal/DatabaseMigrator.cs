using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace BikeHub.Dal;

public sealed class DatabaseMigrator(IDbContextFactory<BikeHubDbContext> contexts)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await DatabaseSchema.EnsureAsync(context, cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }
}

public sealed class DatabaseBootstrapper(IDbContextFactory<BikeHubDbContext> contexts)
{
    public async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var creator = context.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(cancellationToken))
        {
            await creator.CreateAsync(cancellationToken);
        }
    }
}
