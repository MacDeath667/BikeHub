using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Services;

public sealed class MediaCleanup(IDbContextFactory<BikeHubDbContext> contexts, TimeProvider clock,
    IConfiguration settings, IHostEnvironment environment)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        // Lock candidates before checking references. Writers lock the same rows until their save commits.
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var cutoff = clock.GetUtcNow().AddDays(-1);
            var candidates = await db.Media.FromSqlInterpolated($"""
                SELECT * FROM media_assets WHERE created_at < {cutoff}
                AND NOT EXISTS (SELECT 1 FROM auth_accounts a WHERE a.avatar_id = media_assets.id)
                AND NOT EXISTS (SELECT 1 FROM bikes b WHERE b.image_id = media_assets.id OR media_assets.id = ANY(b.image_ids))
                AND NOT EXISTS (SELECT 1 FROM posts p WHERE p.image_id = media_assets.id OR media_assets.id = ANY(p.image_ids))
                AND NOT EXISTS (SELECT 1 FROM communities g WHERE g.image_id = media_assets.id)
                AND NOT EXISTS (SELECT 1 FROM group_entries e WHERE media_assets.id = ANY(e.image_ids))
                ORDER BY id LIMIT 100 FOR UPDATE SKIP LOCKED
                """).ToListAsync(cancellationToken);
            foreach (var media in candidates)
            {
                var id = media.Id;
                var used = await db.Accounts.AnyAsync(a => a.AvatarId == id, cancellationToken) ||
                    await db.Bikes.AnyAsync(b => b.ImageId == id || b.ImageIds.Contains(id), cancellationToken) ||
                    await db.Posts.AnyAsync(p => p.ImageId == id || p.ImageIds.Contains(id), cancellationToken) ||
                    await db.Communities.AnyAsync(g => g.ImageId == id, cancellationToken) ||
                    await db.GroupEntries.AnyAsync(e => e.ImageIds.Contains(id), cancellationToken);
                if (!used)
                {
                    db.MediaDeletions.Add(new MediaDeletion { Id = id });
                    db.Media.Remove(media);
                }
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        var root = Path.GetFullPath(settings["Media:RootPath"] ?? Path.Combine(environment.ContentRootPath, "App_Data", "media"));
        if (Directory.Exists(root))
        {
            var cutoff = clock.GetUtcNow().AddDays(-1).UtcDateTime;
            foreach (var file in Directory.EnumerateFiles(root).Take(10000))
            {
                var name = Path.GetFileName(file);
                var suffix = name.EndsWith(".webp.tmp", StringComparison.Ordinal) ? ".webp.tmp" : ".webp";
                if (name.EndsWith(suffix, StringComparison.Ordinal) && Guid.TryParseExact(name[..^suffix.Length], "N", out var id) &&
                    File.GetLastWriteTimeUtc(file) < cutoff && !await db.Media.AnyAsync(m => m.Id == id, cancellationToken))
                {
                    File.Delete(file);
                }
            }
        }
        foreach (var id in await db.MediaDeletions.Select(d => d.Id).Take(100).ToListAsync(cancellationToken))
        {
            // Only generated file names; rows survive failures and are retried after restart.
            File.Delete(Path.Combine(root, id.ToString("N") + ".webp"));
            File.Delete(Path.Combine(root, id.ToString("N") + ".webp.tmp"));
            await db.MediaDeletions.Where(d => d.Id == id).ExecuteDeleteAsync(cancellationToken);
        }
    }
}

public sealed class MediaCleanupWorker(IServiceScopeFactory scopes, ILogger<MediaCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<MediaCleanup>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogWarning("Media cleanup failed: {ErrorType}", error.GetType().Name); }
            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                return;
            }
        }
    }
}
