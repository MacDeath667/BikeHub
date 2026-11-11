using System.Text.RegularExpressions;
using BikeHub.Api.Authentication;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BikeHub.Api.Services;

// Each operation owns its DbContext. All viewer-specific relationships are queried from PostgreSQL.
public sealed class PostgresCommunityService(IDbContextFactory<BikeHubDbContext> contexts,
    ICurrentUser current, TimeProvider clock, IConfiguration configuration) :
    IFeedService, IBikeService, IPostService, IUserService, ICommunityService
{
    private const string SignInTitle = "Войдите в аккаунт.";
    private const string LongSearchTitle = "Поисковый запрос слишком длинный.";
    private const string HiddenTitle = "Материал скрыт модератором.";
    private const string OwnBikeTitle = "Выберите велосипед из своего гаража.";
    private const string RequiredTitle = "Заполните обязательное поле.";
    private const string MaterialTypeTitle = "Укажите тип материала.";
    private const string ProtectedModeratorTitle = "Нельзя заблокировать модератора.";
    private const string InvalidPhotosTitle = "Выберите до 10 разных фотографий.";
    private const string MissingTitle = "Материал не найден.";
    private const string OwnContentTitle = "Изменять можно только свои материалы.";
    private const string MediaOwnershipTitle = "Выберите свою загруженную фотографию.";
    private const string ModeratorTitle = "Необходимы права модератора.";
    private const string HandleTakenTitle = "Этот адрес профиля уже занят.";
    private const string ProfileColor = "green";
    private readonly string[] moderators = (configuration.GetSection("Moderation:ModeratorEmails").Get<string[]>() ?? [])
        .Select(x => x.Trim().ToLowerInvariant()).ToArray();
    private Guid? Viewer => current.Id;
    public bool IsModerator => current.Account is { } account && moderators.Contains(account.Email);
    private AuthAccount Actor() => current.Account ?? throw new CommunityProblem(401, SignInTitle);

    internal IQueryable<UserDto> Users(BikeHubDbContext db) => db.Accounts.AsNoTracking().Where(a => !a.IsBlocked)
        .Select(a => new UserDto
        {
            Id = a.Id,
            Name = a.DisplayName,
            Handle = a.Handle ?? a.Id.ToString().Replace("-", ""),
            City = a.City,
            Initials = a.DisplayName.Substring(0, 1).ToUpper(),
            Color = ProfileColor,
            Bio = a.Bio,
            Followers = db.Relations.Count(r => r.TargetId == a.Id && r.Kind == RelationKind.Follow && db.Accounts.Any(u => u.Id == r.UserId && !u.IsBlocked)),
            IsFollowing = Viewer.HasValue && db.Relations.Any(r => r.UserId == Viewer && r.TargetId == a.Id && r.Kind == RelationKind.Follow),
            BikesCount = db.Bikes.Count(b => b.OwnerId == a.Id && !b.IsDeleted && !b.IsHidden),
            PostsCount = db.Posts.Count(p => p.AuthorId == a.Id && !p.IsDeleted && !p.IsHidden && db.Bikes.Any(b => b.Id == p.BikeId && !b.IsDeleted && !b.IsHidden)),
            AvatarUrl = a.AvatarId.HasValue ? Constants.MediaPathPrefix + a.AvatarId.Value.ToString() : null,
            IsModerator = moderators.Contains(a.Email)
        });

    private IQueryable<BikeDto> Bikes(BikeHubDbContext db) =>
        from b in db.Bikes.AsNoTracking()
        join u in Users(db) on b.OwnerId equals u.Id
        where !b.IsDeleted && !b.IsHidden
        select new BikeDto
        {
            Id = b.Id,
            OwnerId = b.OwnerId,
            Name = b.Name,
            Description = b.Description ?? string.Empty,
            Brand = b.Brand,
            Category = (BikeCategory)b.Category,
            Year = b.Year,
            Mileage = b.Mileage,
            ImageUrl = b.ImageId.HasValue ? Constants.MediaPathPrefix + b.ImageId.Value.ToString() :
                b.Category == (int)BikeCategory.Road ? Constants.Images.Road :
                b.Category == (int)BikeCategory.Mtb ? Constants.Images.Trail :
                b.Category == (int)BikeCategory.City ? Constants.Images.City : Constants.Images.Gravel,
            Color = string.Empty,
            ImageId = b.ImageId,
            ImageIds = b.ImageIds,
            Owner = u
        };

    private IQueryable<PostDto> Posts(BikeHubDbContext db) =>
        from p in db.Posts.AsNoTracking()
        join b in Bikes(db) on p.BikeId equals b.Id
        where !p.IsDeleted && !p.IsHidden
        select new PostDto
        {
            Id = p.Id,
            Author = b.Owner,
            Bike = b,
            Title = p.Title,
            Content = p.Content,
            Category = p.Category,
            ImageUrl = p.ImageId.HasValue ? Constants.MediaPathPrefix + p.ImageId.Value.ToString() : b.ImageUrl,
            Tags = p.Tags,
            CreatedAt = p.CreatedAt,
            Likes = db.Relations.Count(r => r.TargetId == p.Id && r.Kind == RelationKind.Like && db.Accounts.Any(u => u.Id == r.UserId && !u.IsBlocked)),
            CommentsCount = db.Comments.Count(c => c.PostId == p.Id && !c.IsDeleted && !c.IsHidden && db.Accounts.Any(u => u.Id == c.AuthorId && !u.IsBlocked)),
            IsLiked = Viewer.HasValue && db.Relations.Any(r => r.UserId == Viewer && r.TargetId == p.Id && r.Kind == RelationKind.Like),
            ImageId = p.ImageId,
            ImageIds = p.ImageIds,
            IsSaved = Viewer.HasValue && db.Relations.Any(r => r.UserId == Viewer && r.TargetId == p.Id && r.Kind == RelationKind.Bookmark)
        };

    private static Page<T> Paginate<T>(IQueryable<T> source, int page, int size)
    {
        page = Math.Clamp(page, 1, 1000000);
        size = Math.Clamp(size, 1, 50);
        return new(source.Skip((page - 1) * size).Take(size).ToList(), source.Count(), page, size);
    }

    private static string? Search(string? text)
    {
        if (text?.Length > 200)
        {
            throw new CommunityProblem(400, LongSearchTitle);
        }
        return string.IsNullOrWhiteSpace(text) ? null : "%" + text.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
    }

    public Page<PostDto> GetFeed(string? tab, BikeCategory? category, string? query, int page, int pageSize)
    {
        using var db = contexts.CreateDbContext();
        var posts = Posts(db);
        if (tab == Constants.FeedTabs.Following)
        {
            posts = posts.Where(p => p.Author.IsFollowing);
        }
        if (tab == Constants.FeedTabs.Saved)
        {
            posts = posts.Where(p => p.IsSaved);
        }
        if (category.HasValue)
        {
            posts = posts.Where(p => p.Bike.Category == category.Value);
        }
        if (Search(query) is { } search)
        {
            posts = posts.Where(p => EF.Functions.ILike(p.Title + " " + p.Content + " " + p.Bike.Name + " " + p.Author.Name, search));
        }
        return Paginate(tab == Constants.FeedTabs.Popular ? posts.OrderByDescending(p => p.Likes).ThenByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            : posts.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id), page, pageSize);
    }

    public OverviewDto GetOverview()
    {
        using var db = contexts.CreateDbContext();
        var tags = Posts(db).OrderByDescending(p => p.CreatedAt).Take(100).Select(p => p.Tags).ToList()
            .SelectMany(x => x).GroupBy(x => x).OrderByDescending(x => x.Count()).Select(x => x.Key).Take(10).ToArray();
        return new(Users(db).Count(), Bikes(db).Count(), Posts(db).Count(), tags, Bikes(db).OrderByDescending(b => b.Id).FirstOrDefault());
    }

    public Page<BikeDto> GetBikes(string? query, BikeCategory? category, Guid? ownerId, int page, int pageSize)
    {
        using var db = contexts.CreateDbContext();
        var bikes = Bikes(db);
        if (ownerId.HasValue)
        {
            bikes = bikes.Where(b => b.OwnerId == ownerId);
        }
        if (category.HasValue)
        {
            bikes = bikes.Where(b => b.Category == category);
        }
        if (Search(query) is { } search)
        {
            bikes = bikes.Where(b => EF.Functions.ILike(b.Name + " " + b.Brand + " " + b.Owner.Name, search));
        }
        return Paginate(bikes.OrderByDescending(b => b.Id), page, pageSize);
    }

    public BikeDto? GetBike(Guid id)
    {
        using var db = contexts.CreateDbContext();
        return Bikes(db).SingleOrDefault(b => b.Id == id);
    }

    public BikeDto CreateBike(CreateBikeRequest request)
    {
        using var db = contexts.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();
        var actor = Actor();
        // Legacy bikes have no owner FK: hold the account row until creation commits.
        if (!db.Accounts.FromSqlInterpolated($"SELECT * FROM auth_accounts WHERE id = {actor.Id} FOR KEY SHARE").Any())
        {
            throw new CommunityProblem(401, SignInTitle);
        }
        var images = CheckImages(db, request.ImageIds, request.ImageId, actor.Id);
        var bike = new Bike
        {
            Id = Guid.CreateVersion7(),
            OwnerId = actor.Id,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            Brand = request.Brand.Trim(),
            Category = (int)request.Category,
            Year = request.Year,
            Mileage = request.Mileage,
            ImageId = images.Length > 0 ? images[0] : null,
            ImageIds = images,
            CreatedAt = clock.GetUtcNow()
        };
        db.Bikes.Add(bike);
        db.SaveChanges(); transaction.Commit();
        return Bikes(db).Single(b => b.Id == bike.Id);
    }

    public BikeDto UpdateBike(Guid id, CreateBikeRequest request)
    {
        using var db = contexts.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();
        var bike = db.Bikes.SingleOrDefault(b => b.Id == id && !b.IsDeleted) ?? throw new CommunityProblem(404, MissingTitle);
        Own(bike.OwnerId);
        if (bike.IsHidden)
        {
            throw new CommunityProblem(409, HiddenTitle);
        }
        var images = CheckImages(db, request.ImageIds, request.ImageId, bike.OwnerId);
        bike.Name = request.Name.Trim(); bike.Description = request.Description?.Trim() ?? string.Empty;
        bike.Brand = request.Brand.Trim(); bike.Category = (int)request.Category; bike.Year = request.Year;
        bike.Mileage = request.Mileage; bike.ImageId = images.Length > 0 ? images[0] : null; bike.ImageIds = images;
        db.SaveChanges(); transaction.Commit();
        return Bikes(db).SingleOrDefault(b => b.Id == id) ?? throw new CommunityProblem(409, HiddenTitle);
    }

    public void DeleteBike(Guid id)
    {
        using var db = contexts.CreateDbContext();
        var bike = db.Bikes.SingleOrDefault(b => b.Id == id && !b.IsDeleted) ?? throw new CommunityProblem(404, MissingTitle);
        Own(bike.OwnerId);
        using var transaction = db.Database.BeginTransaction();
        bike.IsDeleted = true; bike.ImageId = null; bike.ImageIds = [];
        db.Posts.Where(p => p.BikeId == id).ExecuteUpdate(u => u.SetProperty(p => p.IsDeleted, true)
            .SetProperty(p => p.ImageId, (Guid?)null).SetProperty(p => p.ImageIds, Array.Empty<Guid>()));
        db.SaveChanges(); transaction.Commit();
    }

    public Page<PostDto>? GetHistory(Guid id, int page, int pageSize)
    {
        using var db = contexts.CreateDbContext();
        return !Bikes(db).Any(b => b.Id == id) ? null : Paginate(Posts(db).Where(p => p.Bike.Id == id)
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id), page, pageSize);
    }

    public PostDto? GetPost(Guid id)
    {
        using var db = contexts.CreateDbContext();
        return Posts(db).SingleOrDefault(p => p.Id == id);
    }

    public PostDto? CreatePost(CreatePostRequest request)
    {
        using var db = contexts.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();
        var actor = Actor();
        if (!Bikes(db).Any(b => b.Id == request.BikeId && b.OwnerId == actor.Id))
        {
            return null;
        }
        var images = CheckImages(db, request.ImageIds, request.ImageId, actor.Id);
        var post = new CommunityPost
        {
            Id = Guid.CreateVersion7(),
            AuthorId = actor.Id,
            BikeId = request.BikeId,
            Title = request.Title.Trim(),
            Content = request.Content.Trim(),
            Category = request.Category,
            Tags = Tags(request.Content),
            ImageId = images.Length > 0 ? images[0] : null,
            ImageIds = images,
            CreatedAt = clock.GetUtcNow()
        };
        db.Posts.Add(post); db.SaveChanges(); transaction.Commit();
        return Posts(db).Single(p => p.Id == post.Id);
    }

    private static string[] Tags(string content) => Regex.Matches(content, @"#([\p{L}\p{N}_-]{1,40})")
        .Select(x => x.Groups[1].Value.ToLowerInvariant()).Distinct().Take(10).ToArray();

    public PostDto UpdatePost(Guid id, CreatePostRequest request)
    {
        using var db = contexts.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();
        var actor = Actor();
        var post = db.Posts.SingleOrDefault(p => p.Id == id && !p.IsDeleted) ?? throw new CommunityProblem(404, MissingTitle);
        Own(post.AuthorId);
        if (post.IsHidden)
        {
            throw new CommunityProblem(409, HiddenTitle);
        }
        if (!Bikes(db).Any(b => b.Id == request.BikeId && b.OwnerId == actor.Id))
        {
            throw new CommunityProblem(400, OwnBikeTitle);
        }
        var images = CheckImages(db, request.ImageIds, request.ImageId, actor.Id);
        post.Title = request.Title.Trim(); post.Content = request.Content.Trim(); post.Category = request.Category;
        post.BikeId = request.BikeId; post.ImageId = images.Length > 0 ? images[0] : null; post.ImageIds = images; post.Tags = Tags(request.Content); post.UpdatedAt = clock.GetUtcNow();
        db.SaveChanges(); transaction.Commit();
        return Posts(db).SingleOrDefault(p => p.Id == id) ?? throw new CommunityProblem(409, HiddenTitle);
    }

    public void DeletePost(Guid id)
    {
        using var db = contexts.CreateDbContext();
        var post = db.Posts.SingleOrDefault(p => p.Id == id && !p.IsDeleted) ?? throw new CommunityProblem(404, MissingTitle);
        Own(post.AuthorId); post.IsDeleted = true; post.ImageId = null; post.ImageIds = []; db.SaveChanges();
    }

    public PostDto? SetLike(Guid id, bool active) => SetPostRelation(id, RelationKind.Like, active);
    public PostDto? SetBookmark(Guid id, bool active) => SetPostRelation(id, RelationKind.Bookmark, active);
    private PostDto? SetPostRelation(Guid id, RelationKind kind, bool active)
    {
        using var db = contexts.CreateDbContext();
        if (!Posts(db).Any(p => p.Id == id))
        {
            return null;
        }
        Relation(db, id, kind, active);
        return Posts(db).Single(p => p.Id == id);
    }

    public Page<CommentDto>? GetComments(Guid id, int page, int pageSize)
    {
        using var db = contexts.CreateDbContext();
        if (!Posts(db).Any(p => p.Id == id))
        {
            return null;
        }
        var comments = from c in db.Comments.AsNoTracking()
                       join u in Users(db) on c.AuthorId equals u.Id
                       where c.PostId == id && !c.IsDeleted && !c.IsHidden
                       orderby c.CreatedAt descending, c.Id descending
                       select new CommentDto(c.Id, u, c.Text, c.CreatedAt);
        return Paginate(comments, page, pageSize);
    }

    public CommentDto? AddComment(Guid id, CreateCommentRequest request)
    {
        using var db = contexts.CreateDbContext();
        if (!Posts(db).Any(p => p.Id == id))
        {
            return null;
        }
        var actor = Actor();
        var comment = new CommunityComment
        {
            Id = Guid.CreateVersion7(),
            PostId = id,
            AuthorId = actor.Id,
            Text = RequiredText(request.Text),
            CreatedAt = clock.GetUtcNow()
        };
        db.Comments.Add(comment); db.SaveChanges();
        return new(comment.Id, Users(db).Single(u => u.Id == actor.Id), comment.Text, comment.CreatedAt);
    }

    public CommentDto UpdateComment(Guid postId, Guid id, CreateCommentRequest request)
    {
        using var db = contexts.CreateDbContext();
        var comment = db.Comments.SingleOrDefault(c => c.Id == id && c.PostId == postId && !c.IsDeleted) ?? throw new CommunityProblem(404, MissingTitle);
        Own(comment.AuthorId);
        if (!Posts(db).Any(p => p.Id == postId) || comment.IsHidden)
        {
            throw new CommunityProblem(404, MissingTitle);
        }
        comment.Text = RequiredText(request.Text); comment.UpdatedAt = clock.GetUtcNow(); db.SaveChanges();
        return new(comment.Id, Users(db).Single(u => u.Id == comment.AuthorId), comment.Text, comment.CreatedAt);
    }

    public void DeleteComment(Guid postId, Guid id)
    {
        using var db = contexts.CreateDbContext();
        var comment = db.Comments.SingleOrDefault(c => c.Id == id && c.PostId == postId && !c.IsDeleted) ?? throw new CommunityProblem(404, MissingTitle);
        Own(comment.AuthorId); comment.IsDeleted = true; db.SaveChanges();
    }

    public UserDto GetCurrentUser()
    {
        using var db = contexts.CreateDbContext();
        var id = Actor().Id;
        return Users(db).Single(u => u.Id == id);
    }

    public Task<UserDto?> GetUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = contexts.CreateDbContext();
        return FindUserAsync(db, id, cancellationToken);
    }
    private async Task<UserDto?> FindUserAsync(BikeHubDbContext db, Guid id, CancellationToken cancellationToken)
    {
        await using (db)
        {
            return await Users(db).SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        }
    }

    public UserDto UpdateProfile(UpdateProfileRequest request)
    {
        using var db = contexts.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();
        var id = Actor().Id;
        CheckImage(db, request.AvatarId, id);
        var account = db.Accounts.Single(a => a.Id == id);
        if (db.Accounts.Any(a => a.Id != id && (a.Handle == request.Handle ||
            (a.Handle == null && a.Id.ToString().Replace("-", "") == request.Handle))))
        {
            throw new CommunityProblem(409, HandleTakenTitle);
        }
        account.DisplayName = RequiredText(request.Name); account.Handle = request.Handle;
        account.City = request.City?.Trim() ?? string.Empty; account.Bio = request.Bio?.Trim() ?? string.Empty; account.AvatarId = request.AvatarId;
        try { db.SaveChanges(); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new CommunityProblem(409, HandleTakenTitle);
        }
        transaction.Commit();
        return Users(db).Single(u => u.Id == id);
    }

    public UserDto? SetFollowing(Guid id, bool active)
    {
        using var db = contexts.CreateDbContext();
        var actor = Actor();
        if (actor.Id == id || !Users(db).Any(u => u.Id == id))
        {
            return null;
        }
        Relation(db, id, RelationKind.Follow, active);
        return Users(db).Single(u => u.Id == id);
    }

    public IReadOnlyList<CommunityDto> GetCommunities(string? query)
    {
        using var db = contexts.CreateDbContext();
        return Groups(db, query).ToList();
    }
    private IQueryable<CommunityDto> Groups(BikeHubDbContext db, string? query)
    {
        var groups = db.Communities.AsNoTracking().Where(g => !g.IsDeleted && !g.IsHidden);
        if (Search(query) is { } search)
        {
            groups = groups.Where(g => EF.Functions.ILike(g.Name + " " + g.Description, search));
        }
        return groups.OrderBy(g => g.Name).Select(g => new CommunityDto
        {
            Id = g.Id,
            Name = g.Name,
            Description = g.Description,
            Category = (BikeCategory)g.Category,
            ImageUrl = g.ImageId.HasValue ? Constants.MediaPathPrefix + g.ImageId.Value.ToString() : g.ImageUrl,
            OwnerId = g.OwnerId,
            ImageId = g.ImageId,
            Members = db.Relations.Count(r => r.Kind == RelationKind.Membership && r.TargetId == g.Id && db.Accounts.Any(a => a.Id == r.UserId && !a.IsBlocked)),
            IsJoined = Viewer.HasValue && db.Relations.Any(r => r.Kind == RelationKind.Membership && r.UserId == Viewer && r.TargetId == g.Id)
        });
    }
    public CommunityDto? SetMembership(Guid id, bool active)
    {
        using var db = contexts.CreateDbContext();
        if (!db.Communities.Any(g => g.Id == id && !g.IsDeleted && !g.IsHidden))
        {
            return null;
        }
        Relation(db, id, RelationKind.Membership, active);
        return Groups(db, null).Single(g => g.Id == id);
    }

    private void Relation(BikeHubDbContext db, Guid id, RelationKind kind, bool active)
    {
        var actor = Actor().Id;
        if (active)
        {
            db.Database.ExecuteSqlInterpolated($"INSERT INTO community_relations (user_id, target_id, kind) VALUES ({actor}, {id}, {(int)kind}) ON CONFLICT DO NOTHING");
        }
        else
        {
            db.Relations.Where(r => r.UserId == actor && r.TargetId == id && r.Kind == kind).ExecuteDelete();
        }
    }

    private void Own(Guid owner)
    {
        if (Actor().Id != owner)
        {
            throw new CommunityProblem(403, OwnContentTitle);
        }
    }
    internal static Guid[] CheckImages(BikeHubDbContext db, Guid[]? ids, Guid? legacyCover, Guid owner)
    {
        var images = ids ?? (legacyCover.HasValue ? [legacyCover.Value] : []);
        if (images.Length > BikeHub.Application.Constants.MaximumContentPhotos || images.Distinct().Count() != images.Length || images.Contains(Guid.Empty))
        {
            throw new CommunityProblem(400, InvalidPhotosTitle);
        }
        if (db.Media.FromSqlInterpolated($"SELECT * FROM media_assets WHERE id = ANY({images}) ORDER BY id FOR UPDATE")
            .Count(m => m.OwnerId == owner && !m.IsDeleted) != images.Length)
        {
            throw new CommunityProblem(400, MediaOwnershipTitle);
        }
        return images;
    }

    private static void CheckImage(BikeHubDbContext db, Guid? id, Guid owner)
    {
        if (id.HasValue && !db.Media.FromSqlInterpolated($"SELECT * FROM media_assets WHERE id = {id.Value} FOR UPDATE")
            .Any(m => m.OwnerId == owner && !m.IsDeleted))
        {
            throw new CommunityProblem(400, MediaOwnershipTitle);
        }
    }
    private static string RequiredText(string text) => !string.IsNullOrWhiteSpace(text)
        ? text.Trim() : throw new CommunityProblem(400, RequiredTitle);

    public ReportDto Report(ReportRequest request)
    {
        using var db = contexts.CreateDbContext();
        var actor = Actor().Id;
        var exists = request.TargetType switch
        {
            ReportTarget.Bike => Bikes(db).Any(b => b.Id == request.TargetId),
            ReportTarget.Post => Posts(db).Any(p => p.Id == request.TargetId),
            ReportTarget.Comment => db.Comments.Any(c => c.Id == request.TargetId && !c.IsDeleted && !c.IsHidden && Posts(db).Any(p => p.Id == c.PostId)),
            ReportTarget.User => Users(db).Any(u => u.Id == request.TargetId),
            ReportTarget.Group => db.Communities.Any(g => g.Id == request.TargetId && !g.IsDeleted && !g.IsHidden),
            ReportTarget.GroupEntry => db.GroupEntries.Any(e => e.Id == request.TargetId && !e.IsDeleted && !e.IsHidden && db.Communities.Any(g => g.Id == e.GroupId && !g.IsDeleted && !g.IsHidden)),
            ReportTarget.GroupReply => db.GroupReplies.Any(r => r.Id == request.TargetId && !r.IsDeleted && !r.IsHidden && db.GroupEntries.Any(e => e.Id == r.EntryId && !e.IsDeleted && !e.IsHidden && db.Communities.Any(g => g.Id == e.GroupId && !g.IsDeleted && !g.IsHidden))),
            ReportTarget.Site => request.TargetId == Guid.Empty,
            _ => false
        };
        if (!exists)
        {
            throw new CommunityProblem(404, MissingTitle);
        }
        var existing = db.Reports.SingleOrDefault(r => r.ReporterId == actor && r.TargetId == request.TargetId && r.TargetType == request.TargetType && !r.Resolved);
        if (existing is not null)
        {
            return ReportView(existing);
        }
        var report = new ModerationReport
        {
            Id = Guid.CreateVersion7(),
            ReporterId = actor,
            TargetId = request.TargetId,
            TargetType = request.TargetType,
            Reason = RequiredText(request.Reason),
            CreatedAt = clock.GetUtcNow()
        };
        db.Reports.Add(report);
        try { db.SaveChanges(); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            report = db.Reports.Single(r => r.ReporterId == actor && r.TargetId == request.TargetId && r.TargetType == request.TargetType && !r.Resolved);
        }
        return ReportView(report);
    }
    private static ReportDto ReportView(ModerationReport report) => new(report.Id, report.TargetType, report.TargetId,
        report.Reason, report.CreatedAt, report.Resolved, report.Resolution, report.ReporterId);
    private void RequireModerator()
    {
        if (!IsModerator)
        {
            throw new CommunityProblem(403, ModeratorTitle);
        }
    }
    public Page<ReportDto> Reports(bool resolved, int page, int pageSize)
    {
        RequireModerator();
        using var db = contexts.CreateDbContext();
        var query = db.Reports.AsNoTracking().Where(r => r.Resolved == resolved).OrderBy(r => r.CreatedAt).ThenBy(r => r.Id);
        var result = Paginate(query, page, pageSize);
        return new(result.Items.Select(ReportView).ToArray(), result.Total, result.PageNumber, result.PageSize);
    }
    public void ResolveReport(Guid id, string resolution)
    {
        RequireModerator();
        using var db = contexts.CreateDbContext();
        var report = db.Reports.SingleOrDefault(r => r.Id == id) ?? throw new CommunityProblem(404, MissingTitle);
        report.Resolved = true; report.Resolution = RequiredText(resolution); report.ResolvedBy = Actor().Id; db.SaveChanges();
    }
    public void SetVisibility(ReportTarget type, Guid id, bool hidden)
    {
        RequireModerator();
        using var db = contexts.CreateDbContext();
        var count = type switch
        {
            ReportTarget.Bike => db.Bikes.Where(x => x.Id == id && !x.IsDeleted).ExecuteUpdate(u => u.SetProperty(x => x.IsHidden, hidden)),
            ReportTarget.Post => db.Posts.Where(x => x.Id == id && !x.IsDeleted).ExecuteUpdate(u => u.SetProperty(x => x.IsHidden, hidden)),
            ReportTarget.Comment => db.Comments.Where(x => x.Id == id && !x.IsDeleted).ExecuteUpdate(u => u.SetProperty(x => x.IsHidden, hidden)),
            ReportTarget.Group => db.Communities.Where(x => x.Id == id && !x.IsDeleted).ExecuteUpdate(u => u.SetProperty(x => x.IsHidden, hidden)),
            ReportTarget.GroupEntry => db.GroupEntries.Where(x => x.Id == id && !x.IsDeleted).ExecuteUpdate(u => u.SetProperty(x => x.IsHidden, hidden)),
            ReportTarget.GroupReply => db.GroupReplies.Where(x => x.Id == id && !x.IsDeleted).ExecuteUpdate(u => u.SetProperty(x => x.IsHidden, hidden)),
            _ => throw new CommunityProblem(400, MaterialTypeTitle)
        };
        if (count == 0)
        {
            throw new CommunityProblem(404, MissingTitle);
        }
    }
    public void BlockUser(Guid id, bool blocked)
    {
        RequireModerator();
        using var db = contexts.CreateDbContext();
        var account = db.Accounts.SingleOrDefault(a => a.Id == id) ?? throw new CommunityProblem(404, MissingTitle);
        if (id == Actor().Id || moderators.Contains(account.Email))
        {
            throw new CommunityProblem(409, ProtectedModeratorTitle);
        }
        using var transaction = db.Database.BeginTransaction();
        account.IsBlocked = blocked; db.SaveChanges();
        if (blocked)
        {
            db.Sessions.Where(s => s.AccountId == id).ExecuteDelete();
        }
        transaction.Commit();
    }
}
