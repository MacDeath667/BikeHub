using System.Net;
using System.Net.Http.Json;
using BikeHub.Api.Authentication;
using BikeHub.Api.Services;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BikeHub.Api.Tests;

[Collection("Postgres persistence")]
public sealed class DraftTests
{
    private static AuthFactory App(PersistenceTestDatabase database, string? root = null)
    {
        var app = new AuthFactory { Database = database };
        app.Settings["Authentication:Store"] = "Postgres"; app.Settings["Community:Store"] = "Postgres";
        if (root is not null)
        {
            app.Settings["Media:RootPath"] = root;
        }
        return app;
    }
    private static DraftState State(Guid[]? images = null) => new("Draft title", "Private unfinished content", "ride", null, images ?? [],
        new MapRoute("Route", [], [new RouteMarker(55.75, 37.62, "Start")]));
    private static async Task<HttpResponseMessage> Send(HttpClient browser, HttpMethod method, string scope, object? body = null, long? revision = null)
    {
        var session = await browser.GetFromJsonAsync<AuthBootstrap>("/api/auth/session");
        using var request = new HttpRequestMessage(method, "/api/drafts/" + scope + (revision.HasValue ? "?revision=" + revision : ""));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        request.Headers.Add("X-CSRF-TOKEN", session!.CsrfToken);
        return await browser.SendAsync(request);
    }
    private static Task<HttpResponseMessage> Save(HttpClient browser, string scope, long revision, DraftState state) =>
        Send(browser, HttpMethod.Put, scope, new SaveDraftRequest(revision, state));
    private static async Task<MediaDto> Upload(HttpClient browser)
    {
        using var image = new MagickImage(MagickColors.Green, 40, 40);
        using var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(image.ToByteArray(MagickFormat.Png)), "file", "photo.png");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/media") { Content = form };
        request.Headers.Add("X-CSRF-TOKEN", (await browser.GetFromJsonAsync<AuthBootstrap>("/api/auth/session"))!.CsrfToken);
        using var result = await browser.SendAsync(request); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        return (await result.Content.ReadFromJsonAsync<MediaDto>())!;
    }
    private static async Task Cleanup(AuthFactory app)
    {
        using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<MediaCleanup>().RunAsync(default);
    }
    [Fact]
    public async Task Drafts_are_private_persistent_and_protected_from_stale_or_resurrected_autosaves()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var owner = app.Browser(); using var other = app.Browser(); using var guest = app.Browser();
        (await AuthTestClient.Login(owner, "draft-owner@example.test")).Response.Dispose();
        (await AuthTestClient.Login(other, "draft-other@example.test")).Response.Dispose();
        using var saved = await Save(owner, "post-new", 0, State()); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(1, (await saved.Content.ReadFromJsonAsync<DraftDto>())!.Revision);
        var hidden = (await other.GetFromJsonAsync<DraftDto>("/api/drafts/post-new"))!; Assert.Null(hidden.State); Assert.Equal(0, hidden.Revision);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/drafts/post-new")).StatusCode);
        using var missingCsrf = await owner.PutAsJsonAsync("/api/drafts/post-new", new SaveDraftRequest(1, State())); Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        var writes = await Task.WhenAll(Save(owner, "post-new", 1, State() with { Content = "First tab" }), Save(owner, "post-new", 1, State() with { Content = "Second tab" }));
        try { Assert.Single(writes, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(writes, r => r.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var write in writes) { write.Dispose(); } }
        using var read = await owner.GetAsync("/api/drafts/post-new"); Assert.True(read.Headers.CacheControl!.NoStore);
        var draft = (await read.Content.ReadFromJsonAsync<DraftDto>())!; Assert.Equal(2, draft.Revision); Assert.Single(draft.State!.Route!.Markers);
        await using (var restarted = App(database))
        {
            restarted.Clock.Advance(TimeSpan.FromMinutes(2)); using var again = restarted.Browser();
            (await AuthTestClient.Login(again, "draft-owner@example.test", registering: false)).Response.Dispose();
            Assert.Equal(draft.State.Content, (await again.GetFromJsonAsync<DraftDto>("/api/drafts/post-new"))!.State!.Content);
        }
        using var cleared = await Send(owner, HttpMethod.Delete, "post-new", revision: 2); Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null((await owner.GetFromJsonAsync<DraftDto>("/api/drafts/post-new"))!.State);
        using var late = await Save(owner, "post-new", 2, State()); Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        using var tombstone = await Send(owner, HttpMethod.Delete, "post-late", revision: 0); Assert.Equal(HttpStatusCode.OK, tombstone.StatusCode);
        using var firstLate = await Save(owner, "post-late", 0, State()); Assert.Equal(HttpStatusCode.Conflict, firstLate.StatusCode);
        using var fresh = await Save(owner, "post-new", 3, State()); Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        using var invalidScope = await Save(owner, "bad.scope", 0, State()); Assert.Equal(HttpStatusCode.BadRequest, invalidScope.StatusCode);
        using var invalidState = await Save(owner, "post-invalid", 0, State() with { Title = new string('x', 161) }); Assert.Equal(HttpStatusCode.BadRequest, invalidState.StatusCode);
    }
    [Fact]
    public async Task Draft_photos_remain_private_and_survive_cleanup_until_the_draft_is_cleared_or_expires()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        var root = Path.Combine(Path.GetTempPath(), "bikehub-draft-media-" + Guid.NewGuid());
        try
        {
            await using var app = App(database, root); using var owner = app.Browser(); using var other = app.Browser();
            (await AuthTestClient.Login(owner, "draft-media@example.test")).Response.Dispose(); (await AuthTestClient.Login(other, "draft-thief@example.test")).Response.Dispose();
            var image = await Upload(owner);
            using var saved = await Save(owner, "post-new", 0, State([image.Id])); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            using var stolen = await Save(other, "post-new", 0, State([image.Id])); Assert.Equal(HttpStatusCode.BadRequest, stolen.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(image.ImageUrl)).StatusCode);
            app.Clock.Advance(TimeSpan.FromDays(2)); await Cleanup(app);
            Assert.True(File.Exists(Path.Combine(root, image.Id.ToString("N") + ".webp")));
            using var removed = await Send(owner, HttpMethod.Delete, "post-new", revision: 1); Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
            await Cleanup(app);
            Assert.False(File.Exists(Path.Combine(root, image.Id.ToString("N") + ".webp")));
            var expiring = await Upload(owner);
            using var retained = await Save(owner, "post-retained", 0, State([expiring.Id])); Assert.Equal(HttpStatusCode.OK, retained.StatusCode);
            app.Clock.Advance(TimeSpan.FromDays(31)); await Cleanup(app);
            await using var db = await database.CreateDbContextAsync(); Assert.Empty(await db.Drafts.ToArrayAsync());
            Assert.False(await db.Media.AnyAsync(m => m.Id == expiring.Id)); Assert.False(File.Exists(Path.Combine(root, expiring.Id.ToString("N") + ".webp")));
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }
    [Fact]
    public async Task Draft_quota_is_bounded_and_clearing_a_slot_releases_active_capacity()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var owner = app.Browser(); var (auth, login) = await AuthTestClient.Login(owner, "draft-quota@example.test"); login.Dispose();
        await using (var db = await database.CreateDbContextAsync())
        {
            db.Drafts.AddRange(Enumerable.Range(0, 30).Select(i => new ContentDraft { OwnerId = auth.User!.Id, Scope = "slot-" + i, Revision = 1, StateJson = "{}", UpdatedAt = app.Clock.GetUtcNow() })); await db.SaveChangesAsync();
        }
        using var full = await Save(owner, "new-slot", 0, State()); Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        using var cleared = await Send(owner, HttpMethod.Delete, "slot-0", revision: 1); Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        using var freed = await Save(owner, "new-slot", 0, State()); Assert.Equal(HttpStatusCode.OK, freed.StatusCode);
    }
    [Fact]
    public async Task Account_erasure_removes_its_private_drafts()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var owner = app.Browser(); var (auth, login) = await AuthTestClient.Login(owner, "draft-erasure@example.test"); login.Dispose();
        using var saved = await Save(owner, "post-new", 0, State()); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        app.Clock.Advance(TimeSpan.FromMinutes(2));
        using var challengeResponse = await AuthTestClient.Post(owner, "/api/users/me/deletion"); Assert.Equal(HttpStatusCode.Accepted, challengeResponse.StatusCode);
        var challenge = (await challengeResponse.Content.ReadFromJsonAsync<EmailCodeResponse>())!;
        var code = (await owner.GetFromJsonAsync<DevelopmentCode>($"/api/users/me/deletion/{challenge.ChallengeId}/development"))!.Code;
        using var erased = await AuthTestClient.Post(owner, "/api/users/me/deletion/confirm", new { challengeId = challenge.ChallengeId, code }); Assert.Equal(HttpStatusCode.NoContent, erased.StatusCode);
        await using var db = await database.CreateDbContextAsync(); Assert.False(await db.Drafts.AnyAsync(d => d.OwnerId == auth.User!.Id));
    }
    private sealed record DevelopmentCode(string Code);
}
