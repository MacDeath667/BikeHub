using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BikeHub.Api.Authentication;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Services;

public sealed record DraftState(
    [Required(AllowEmptyStrings = true), StringLength(160)] string Title,
    [Required(AllowEmptyStrings = true), StringLength(20000)] string Content,
    [Required(AllowEmptyStrings = true), StringLength(20)] string Category,
    Guid? BikeId, [Required, MaxLength(BikeHub.Application.Constants.MaximumContentPhotos)] Guid[] ImageIds, MapRoute? Route);
public sealed record SaveDraftRequest([Range(0, long.MaxValue)] long ExpectedRevision, [Required] DraftState State);
public sealed record DraftDto(long Revision, DraftState? State, PhotoDto[] Photos, DateTimeOffset? UpdatedAt);

public sealed class DraftService(IDbContextFactory<BikeHubDbContext> contexts, ICurrentUser current, TimeProvider clock)
{
    private const int MaximumBytes = 2 * 1024 * 1024;
    private const int MaximumActiveDrafts = 30;
    private const int MaximumSlots = 128;
    private const string InvalidScope = "Не удалось определить место сохранения черновика.";
    private const string Conflict = "Черновик изменён в другой вкладке. Сохраните свою версию явно или откройте форму заново.";
    private const string TooLarge = "Черновик слишком большой. Уменьшите маршрут или количество фотографий.";
    private const string TooMany = "Слишком много черновиков. Удалите ненужные и повторите сохранение.";
    private const string SignIn = "Войдите в аккаунт.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private Guid Actor => current.Id ?? throw new CommunityProblem(401, SignIn);
    private static void Validate(string scope)
    {
        if (!Regex.IsMatch(scope, "^[a-z0-9][a-z0-9_-]{0,119}$", RegexOptions.CultureInvariant))
        {
            throw new CommunityProblem(400, InvalidScope);
        }
    }
    private void Lock(BikeHubDbContext db, Guid owner) =>
        db.Database.ExecuteSqlInterpolated($"SELECT pg_advisory_xact_lock(1112099658, hashtext({owner.ToString()}))");
    private DraftDto View(ContentDraft? draft)
    {
        if (draft is null)
        {
            return new(0, null, [], null);
        }
        var state = draft.StateJson is null || draft.UpdatedAt < clock.GetUtcNow().AddDays(-Constants.DraftRetentionDays)
            ? null : JsonSerializer.Deserialize<DraftState>(draft.StateJson, JsonOptions);
        return new(draft.Revision, state, state is null ? [] : draft.ImageIds.Select(id => new PhotoDto(id, Constants.MediaPathPrefix + id)).ToArray(), draft.UpdatedAt);
    }
    public DraftDto Get(string scope)
    {
        Validate(scope); var owner = Actor;
        using var db = contexts.CreateDbContext();
        return View(db.Drafts.AsNoTracking().SingleOrDefault(d => d.OwnerId == owner && d.Scope == scope));
    }
    public DraftDto Save(string scope, SaveDraftRequest request)
    {
        Validate(scope); var owner = Actor;
        RouteService.Save(request.State.Route);
        var json = JsonSerializer.Serialize(request.State, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes)
        {
            throw new CommunityProblem(400, TooLarge);
        }
        using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction();
        var images = PostgresCommunityService.CheckImages(db, request.State.ImageIds, null, owner);
        Lock(db, owner);
        var draft = db.Drafts.SingleOrDefault(d => d.OwnerId == owner && d.Scope == scope);
        if ((draft?.Revision ?? 0) != request.ExpectedRevision)
        {
            throw new CommunityProblem(409, Conflict);
        }
        var cutoff = clock.GetUtcNow().AddDays(-Constants.DraftRetentionDays);
        if ((draft?.StateJson is null || draft.UpdatedAt < cutoff) && db.Drafts.Count(d => d.OwnerId == owner && d.StateJson != null && d.UpdatedAt >= cutoff) >= MaximumActiveDrafts ||
            draft is null && db.Drafts.Count(d => d.OwnerId == owner) >= MaximumSlots)
        {
            throw new CommunityProblem(409, TooMany);
        }
        if (draft is null)
        {
            draft = new ContentDraft { OwnerId = owner, Scope = scope }; db.Drafts.Add(draft);
        }
        draft.Revision++; draft.StateJson = json; draft.ImageIds = images; draft.UpdatedAt = clock.GetUtcNow();
        db.SaveChanges(); transaction.Commit(); return View(draft);
    }
    public DraftDto Clear(string scope, long expectedRevision)
    {
        Validate(scope); var owner = Actor;
        using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction();
        Lock(db, owner);
        var draft = db.Drafts.SingleOrDefault(d => d.OwnerId == owner && d.Scope == scope);
        if (draft is not null && draft.StateJson is null && expectedRevision <= draft.Revision)
        {
            return View(draft);
        }
        if ((draft?.Revision ?? 0) != expectedRevision)
        {
            throw new CommunityProblem(409, Conflict);
        }
        if (draft is null)
        {
            if (db.Drafts.Count(d => d.OwnerId == owner) >= MaximumSlots)
            {
                throw new CommunityProblem(409, TooMany);
            }
            draft = new ContentDraft { OwnerId = owner, Scope = scope }; db.Drafts.Add(draft);
        }
        // Keep a revision tombstone so an old in-flight autosave cannot resurrect a published draft.
        draft.Revision++; draft.StateJson = null; draft.ImageIds = []; draft.UpdatedAt = clock.GetUtcNow();
        db.SaveChanges(); transaction.Commit(); return View(draft);
    }
}
