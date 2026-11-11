using BikeHub.Api.Authentication;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;
using ImageMagick;

namespace BikeHub.Api.Services;

public sealed class MediaStore(IDbContextFactory<BikeHubDbContext> contexts, ICurrentUser current,
    TimeProvider clock, IConfiguration settings, IHostEnvironment environment)
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;
    private const long MaxPixels = 24_000_000;
    private const long OwnerQuotaBytes = 250 * 1024 * 1024;
    private const string SignInTitle = "Войдите в аккаунт.";
    private const string BusyTitle = "Сервер обрабатывает другую фотографию. Попробуйте ещё раз.";
    private const string QuotaTitle = "Лимит фотографий исчерпан. Попробуйте завтра или обратитесь к администратору.";
    private const string InvalidImageTitle = "Выберите фотографию JPEG, PNG или WebP размером до 10 МБ и 24 мегапикселей.";
    private static readonly SemaphoreSlim DecodeGate = new(1);
    static MediaStore()
    {
        ResourceLimits.Memory = 128 * 1024 * 1024;
        ResourceLimits.Disk = 0;
        ResourceLimits.Thread = 1;
        ResourceLimits.Width = 10000;
        ResourceLimits.Height = 10000;
    }
    private string Root => Path.GetFullPath(settings["Media:RootPath"] ?? Path.Combine(environment.ContentRootPath, "App_Data", "media"));
    private string PathFor(Guid id) => Path.Combine(Root, id.ToString("N") + ".webp");

    public async Task<MediaDto> UploadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var owner = current.Id ?? throw new CommunityProblem(401, SignInTitle);
        if (file.Length is <= 0 or > MaxUploadBytes)
        {
            throw new CommunityProblem(400, InvalidImageTitle);
        }
        if (!await DecodeGate.WaitAsync(0, cancellationToken))
        {
            throw new CommunityProblem(429, BusyTitle);
        }
        try
        {
            await using var input = file.OpenReadStream();
            var header = new byte[12];
            var read = await input.ReadAsync(header, cancellationToken);
            var format = read >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff ? MagickFormat.Jpeg :
                read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? MagickFormat.Png :
                read == 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8) ? MagickFormat.WebP :
                throw new CommunityProblem(400, InvalidImageTitle);
            var decoder = new MagickReadSettings { Format = format, FrameCount = 1 };
            input.Position = 0;
            using var image = new MagickImage();
            image.Ping(input, decoder);
            if ((long)image.Width * image.Height > MaxPixels || image.Width > 10000 || image.Height > 10000)
            {
                throw new CommunityProblem(400, InvalidImageTitle);
            }
            input.Position = 0;
            image.Read(input, decoder);
            image.AutoOrient();
            image.Resize(new MagickGeometry(1600, 1600) { Greater = true });
            image.Strip();
            image.Quality = 82;
            await using var output = new MemoryStream();
            image.Write(output, MagickFormat.WebP);
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(1112099654, hashtext({owner.ToString()}))", cancellationToken);
            var assets = db.Media.Where(m => m.OwnerId == owner && !m.IsDeleted);
            var since = clock.GetUtcNow().AddDays(-1);
            if (await assets.Where(m => m.CreatedAt > since).CountAsync(cancellationToken) >= 50 ||
                (await assets.SumAsync(m => (long?)m.Bytes, cancellationToken) ?? 0) + output.Length > OwnerQuotaBytes)
            {
                throw new CommunityProblem(429, QuotaTitle);
            }
            var id = Guid.CreateVersion7();
            Directory.CreateDirectory(Root);
            var path = PathFor(id);
            var temporary = path + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, output.ToArray(), cancellationToken);
                File.Move(temporary, path);
                db.Media.Add(new MediaAsset
                {
                    Id = id,
                    OwnerId = owner,
                    Width = (int)image.Width,
                    Height = (int)image.Height,
                    Bytes = output.Length,
                    CreatedAt = clock.GetUtcNow()
                });
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                File.Delete(temporary);
                File.Delete(path);
                throw;
            }
            return new(id, Constants.MediaPathPrefix + id, (int)image.Width, (int)image.Height);
        }
        catch (MagickException)
        {
            throw new CommunityProblem(400, InvalidImageTitle);
        }
        finally
        {
            DecodeGate.Release();
        }
    }

    public string? Read(Guid id)
    {
        using var db = contexts.CreateDbContext();
        var asset = db.Media.AsNoTracking().SingleOrDefault(m => m.Id == id && !m.IsDeleted);
        if (asset is null || !db.Accounts.Any(a => a.Id == asset.OwnerId && !a.IsBlocked))
        {
            return null;
        }
        var visible = db.Bikes.Where(b => !b.IsDeleted && !b.IsHidden && db.Accounts.Any(a => a.Id == b.OwnerId && !a.IsBlocked));
        var published = db.Accounts.Any(a => a.AvatarId == id && !a.IsBlocked) || visible.Any(b => b.ImageId == id || b.ImageIds.Contains(id)) ||
            db.Posts.Any(p => (p.ImageId == id || p.ImageIds.Contains(id)) && !p.IsDeleted && !p.IsHidden && visible.Any(b => b.Id == p.BikeId));
        published = published || db.Communities.Any(g => g.ImageId == id && !g.IsDeleted && !g.IsHidden) ||
            db.GroupEntries.Any(e => e.ImageIds.Contains(id) && !e.IsDeleted && !e.IsHidden && db.Accounts.Any(a => a.Id == e.AuthorId && !a.IsBlocked) &&
                db.Communities.Any(g => g.Id == e.GroupId && !g.IsDeleted && !g.IsHidden));
        var path = PathFor(id);
        return (published || asset.OwnerId == current.Id) && File.Exists(path) ? path : null;
    }
}
