using System.Net;
using System.Net.Http.Json;
using BikeHub.Api.Authentication;
using BikeHub.Api.Services;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BikeHub.Api.Tests;

[Collection("Postgres persistence")]
public sealed class GroupsAndDeletionTests
{
    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        var session = await client.GetFromJsonAsync<AuthBootstrap>("/api/auth/session");
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("X-CSRF-TOKEN", session!.CsrfToken);
        return await client.SendAsync(request);
    }
    private static AuthFactory App(PersistenceTestDatabase database)
    {
        var app = new AuthFactory { Database = database };
        app.Settings["Authentication:Store"] = "Postgres";
        app.Settings["Community:Store"] = "Postgres";
        app.Settings["Moderation:ModeratorEmails:0"] = "moderator@example.test";
        return app;
    }
    [Fact]
    public async Task Groups_enforce_membership_ownership_reports_and_moderation_for_posts_discussions_and_replies()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database);
        using var owner = app.Browser(); using var other = app.Browser(); using var moderator = app.Browser(); using var guest = app.Browser();
        var (ownerSession, ownerLogin) = await AuthTestClient.Login(owner, "owner@example.test"); ownerLogin.Dispose();
        (await AuthTestClient.Login(other, "member@example.test")).Response.Dispose();
        (await AuthTestClient.Login(moderator, "moderator@example.test")).Response.Dispose();
        object body = new { name = "Gravel club", description = "Weekend rides", category = "gravel" };
        using var created = await Send(owner, HttpMethod.Post, "/api/communities", body);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var group = (await created.Content.ReadFromJsonAsync<CommunityDto>())!;
        Assert.Equal(ownerSession.User!.Id, group.OwnerId); Assert.True(group.IsJoined); Assert.Equal(1, group.Members);
        using var stolen = await Send(other, HttpMethod.Put, $"/api/communities/{group.Id}", body);
        Assert.Equal(HttpStatusCode.Forbidden, stolen.StatusCode);
        var entryBody = new { kind = 2, title = "Where shall we ride?", content = "Suggest a route", imageIds = Array.Empty<Guid>() };
        using var rejected = await Send(other, HttpMethod.Post, $"/api/communities/{group.Id}/entries", entryBody);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        using var join = await Send(other, HttpMethod.Put, $"/api/communities/{group.Id}/membership");
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
        using var post = await Send(other, HttpMethod.Post, $"/api/communities/{group.Id}/entries", entryBody);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var entry = (await post.Content.ReadFromJsonAsync<GroupEntryDto>())!;
        Assert.Equal(1, (await guest.GetFromJsonAsync<Page<GroupEntryDto>>($"/api/communities/{group.Id}/entries?kind=2"))!.Total);
        Assert.Empty((await guest.GetFromJsonAsync<Page<GroupEntryDto>>($"/api/communities/{group.Id}/entries?kind=1"))!.Items);
        using var reply = await Send(owner, HttpMethod.Post, $"/api/group-entries/{entry.Id}/replies", new { text = "Forest route" });
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        Assert.Single((await guest.GetFromJsonAsync<Page<CommentDto>>($"/api/group-entries/{entry.Id}/replies"))!.Items);
        using var forgedEdit = await Send(owner, HttpMethod.Put, $"/api/communities/{group.Id}/entries/{entry.Id}", entryBody);
        Assert.Equal(HttpStatusCode.Forbidden, forgedEdit.StatusCode);
        using var report = await Send(owner, HttpMethod.Post, "/api/reports", new { targetType = 7, targetId = entry.Id, reason = "Check this entry" });
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        using var forbiddenModeration = await Send(owner, HttpMethod.Put, $"/api/moderation/7/{entry.Id}/visibility", new { hidden = true });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenModeration.StatusCode);
        using var hidden = await Send(moderator, HttpMethod.Put, $"/api/moderation/7/{entry.Id}/visibility", new { hidden = true });
        Assert.Equal(HttpStatusCode.NoContent, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/group-entries/{entry.Id}/replies")).StatusCode);
        using var restored = await Send(moderator, HttpMethod.Put, $"/api/moderation/7/{entry.Id}/visibility", new { hidden = false });
        Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
        using var deletedByOwner = await Send(owner, HttpMethod.Delete, $"/api/group-entries/{entry.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deletedByOwner.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/group-entries/{entry.Id}")).StatusCode);
    }
    [Fact]
    public async Task Deletion_requires_session_bound_dedicated_code_erases_owned_data_and_preserves_other_members()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        var root = Path.Combine(Path.GetTempPath(), "bikehub-erasure-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            await using var app = App(database); app.Settings["Media:RootPath"] = root;
            using var owner = app.Browser(); using var other = app.Browser();
            var (account, login) = await AuthTestClient.Login(owner, "erase@example.test"); login.Dispose();
            var (member, memberLogin) = await AuthTestClient.Login(other, "keep@example.test"); memberLogin.Dispose();
            using var created = await Send(owner, HttpMethod.Post, "/api/communities", new { name = "Preserved club", description = "Shared community", category = "city" });
            var group = (await created.Content.ReadFromJsonAsync<CommunityDto>())!;
            using var joined = await Send(other, HttpMethod.Put, $"/api/communities/{group.Id}/membership");
            using var post = await Send(other, HttpMethod.Post, $"/api/communities/{group.Id}/entries", new { kind = 1, title = "Surviving post", content = "Member content", imageIds = Array.Empty<Guid>() });
            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
            var photo = Guid.NewGuid(); var keepPhoto = Guid.NewGuid();
            await using (var db = await database.CreateDbContextAsync())
            {
                db.Media.AddRange(new MediaAsset { Id = photo, OwnerId = account.User!.Id, CreatedAt = DateTimeOffset.UtcNow },
                    new MediaAsset { Id = keepPhoto, OwnerId = member.User!.Id, CreatedAt = DateTimeOffset.UtcNow });
                SeedErasureDomains(db, account.User!.Id, member.User!.Id, app.Clock.GetUtcNow());
                await db.SaveChangesAsync();
            }
            await File.WriteAllTextAsync(Path.Combine(root, photo.ToString("N") + ".webp"), "erased");
            await File.WriteAllTextAsync(Path.Combine(root, keepPhoto.ToString("N") + ".webp"), "preserved");
            app.Clock.Advance(TimeSpan.FromSeconds(61));
            using var request = await Send(owner, HttpMethod.Post, "/api/users/me/deletion");
            Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
            var challenge = (await request.Content.ReadFromJsonAsync<EmailCodeResponse>())!;
            var code = await owner.GetFromJsonAsync<Code>($"/api/users/me/deletion/{challenge.ChallengeId}/development");
            var store = app.Services.GetRequiredService<IAuthenticationStore>();
            var row = await store.FindChallengeAsync(challenge.ChallengeId, default);
            var loginAttempt = await store.ConfirmAsync(row!.Id, row.CodeHash, row.BindingHash, app.Clock.GetUtcNow(),
                app.Clock.GetUtcNow().AddDays(1), 5, default);
            Assert.Equal(ConfirmationFailure.Invalid, loginAttempt.Failure);
            Assert.Null(loginAttempt.Session);
            using var crossSession = await Send(other, HttpMethod.Post, "/api/users/me/deletion/confirm", new { challengeId = challenge.ChallengeId, code = code!.Value });
            Assert.Equal(HttpStatusCode.NotFound, crossSession.StatusCode);
            using var bad = await Send(owner, HttpMethod.Post, "/api/users/me/deletion/confirm", new { challengeId = challenge.ChallengeId, code = code.Value == "000000" ? "111111" : "000000" });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            using var deleted = await Send(owner, HttpMethod.Post, "/api/users/me/deletion/confirm", new { challengeId = challenge.ChallengeId, code = code.Value });
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.Null((await owner.GetFromJsonAsync<AuthBootstrap>("/api/auth/session"))!.User);
            await using (var db = await database.CreateDbContextAsync())
            {
                Assert.False(await db.Accounts.AnyAsync(a => a.Id == account.User!.Id));
                Assert.False(await db.EmailChallenges.AnyAsync(c => c.Email == "erase@example.test"));
                Assert.Null((await db.Communities.SingleAsync(g => g.Id == group.Id)).OwnerId);
                Assert.Single(await db.GroupEntries.ToListAsync());
                Assert.True(await db.MediaDeletions.AnyAsync(m => m.Id == photo));
                Assert.True(await db.Media.AnyAsync(m => m.Id == keepPhoto));
                Assert.Equal(member.User!.Id, Assert.Single(await db.Bikes.ToListAsync()).OwnerId);
                Assert.Equal(member.User.Id, Assert.Single(await db.Rides.ToListAsync()).OwnerId);
                Assert.Equal(member.User.Id, Assert.Single(await db.RideParticipants.ToListAsync()).UserId);
                Assert.Equal(member.User.Id, Assert.Single(await db.LibraryRoutes.ToListAsync()).OwnerId);
                Assert.Empty(await db.SavedRoutes.ToListAsync()); Assert.Empty(await db.RouteCollections.ToListAsync());
                Assert.Empty(await db.RouteReviews.ToListAsync()); Assert.Empty(await db.RideChanges.ToListAsync());
                Assert.Empty(await db.BikeComponents.ToListAsync()); Assert.Empty(await db.MaintenanceRecords.ToListAsync()); Assert.Empty(await db.MaintenanceReminders.ToListAsync());
                var keptReply = Assert.Single(await db.Comments.ToListAsync()); Assert.Equal(member.User.Id, keptReply.AuthorId); Assert.Null(keptReply.ReplyToId);
            }
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MediaCleanup>().RunAsync(default);
            Assert.False(File.Exists(Path.Combine(root, photo.ToString("N") + ".webp")));
            Assert.True(File.Exists(Path.Combine(root, keepPhoto.ToString("N") + ".webp")));
            Assert.NotNull((await other.GetFromJsonAsync<AuthBootstrap>("/api/auth/session"))!.User);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Cleanup_removes_old_unattached_files_but_preserves_fresh_uploads_and_group_galleries()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        var root = Path.Combine(Path.GetTempPath(), "bikehub-cleanup-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            await using var app = App(database); app.Settings["Media:RootPath"] = root;
            using var browser = app.Browser();
            var (account, login) = await AuthTestClient.Login(browser, "photos@example.test"); login.Dispose();
            var loose = Guid.NewGuid(); var fresh = Guid.NewGuid(); var attached = Guid.NewGuid();
            await using (var db = await database.CreateDbContextAsync())
            {
                db.Media.AddRange(new[] { loose, attached, fresh }.Select(id => new MediaAsset { Id = id, OwnerId = account.User!.Id,
                    CreatedAt = app.Clock.GetUtcNow().AddDays(id == fresh ? 0 : -2) }));
                var group = new CommunityGroup { Id = Guid.NewGuid(), Name = "Photos", Description = "Gallery", Category = 3, OwnerId = account.User!.Id };
                db.Communities.Add(group);
                db.GroupEntries.Add(new GroupEntry { Id = Guid.NewGuid(), GroupId = group.Id, AuthorId = account.User.Id, Kind = GroupEntryKind.Post,
                    Title = "Hidden photo", Content = "Keep for moderation", ImageIds = [attached], IsHidden = true, CreatedAt = app.Clock.GetUtcNow() });
                await db.SaveChangesAsync();
            }
            foreach (var id in new[] { loose, attached, fresh })
            {
                await File.WriteAllTextAsync(Path.Combine(root, id.ToString("N") + ".webp"), "photo");
            }
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MediaCleanup>().RunAsync(default);
            Assert.False(File.Exists(Path.Combine(root, loose.ToString("N") + ".webp")));
            Assert.True(File.Exists(Path.Combine(root, attached.ToString("N") + ".webp")));
            Assert.True(File.Exists(Path.Combine(root, fresh.ToString("N") + ".webp")));
            await using var check = await database.CreateDbContextAsync();
            Assert.False(await check.Media.AnyAsync(m => m.Id == loose));
            Assert.Equal(2, await check.Media.CountAsync());
            Assert.Empty(await check.MediaDeletions.ToListAsync());
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed record Code([property: System.Text.Json.Serialization.JsonPropertyName("code")] string Value);
    private static void SeedErasureDomains(BikeHubDbContext db, Guid erased, Guid kept, DateTimeOffset now)
    {
        var removedBike = new Bike { Id = Guid.CreateVersion7(), OwnerId = erased, Name = "Private erasure bicycle" };
        var keptBike = new Bike { Id = Guid.CreateVersion7(), OwnerId = kept, Name = "Preserved bicycle" };
        var post = new CommunityPost { Id = Guid.CreateVersion7(), AuthorId = kept, BikeId = keptBike.Id, Title = "Preserved story", Category = "story", CreatedAt = now };
        var parent = new CommunityComment { Id = Guid.CreateVersion7(), AuthorId = erased, PostId = post.Id, Text = "Erased parent", CreatedAt = now };
        db.Bikes.AddRange(removedBike, keptBike); db.Posts.Add(post); db.Comments.AddRange(parent,
            new CommunityComment { Id = Guid.CreateVersion7(), AuthorId = kept, PostId = post.Id, ReplyToId = parent.Id, Text = "Preserved reply", CreatedAt = now });
        var component = new BikeComponent { Id = Guid.CreateVersion7(), BikeId = removedBike.Id, Kind = BikeComponentKind.Chain, Name = "Private chain", InstalledOn = DateOnly.FromDateTime(now.UtcDateTime), ServiceIntervalKm = 100, Revision = 1, CreatedAt = now, UpdatedAt = now };
        db.BikeComponents.Add(component);
        db.MaintenanceRecords.Add(new MaintenanceRecord { Id = Guid.CreateVersion7(), BikeId = removedBike.Id, ComponentId = component.Id, Kind = MaintenanceKind.Service, Title = "Private cost", CostRub = 1234, PerformedOn = component.InstalledOn, Revision = 1, CreatedAt = now, UpdatedAt = now });
        db.MaintenanceReminders.Add(new MaintenanceReminder { Id = Guid.CreateVersion7(), ComponentId = component.Id, DueMileage = 100, CreatedAt = now });
        var ride = new GroupRide { Id = Guid.CreateVersion7(), OwnerId = erased, Title = "Erased ride", StartsAt = now.AddDays(1), TimeZone = "UTC", Difficulty = RideDifficulty.Easy, Status = RideStatus.Scheduled, Revision = 1, CreatedAt = now, UpdatedAt = now };
        var keptRide = new GroupRide { Id = Guid.CreateVersion7(), OwnerId = kept, Title = "Preserved ride", StartsAt = now.AddDays(1), TimeZone = "UTC", Difficulty = RideDifficulty.Easy, Status = RideStatus.Scheduled, Revision = 1, CreatedAt = now, UpdatedAt = now };
        db.Rides.AddRange(ride, keptRide);
        db.RideParticipants.AddRange(new RideParticipant { RideId = ride.Id, UserId = kept, Attendance = RideAttendance.Going, CreatedAt = now },
            new RideParticipant { RideId = keptRide.Id, UserId = erased, Attendance = RideAttendance.Going, CreatedAt = now },
            new RideParticipant { RideId = keptRide.Id, UserId = kept, Attendance = RideAttendance.Going, CreatedAt = now });
        db.RideChanges.Add(new RideChange { Id = Guid.CreateVersion7(), RideId = ride.Id, Revision = 1, CreatedAt = now });
        var json = RouteJson.Write(new MapRoute("Route", [[new(0, 0), new(0, 1)]], []))!;
        var route = new LibraryRoute { Id = Guid.CreateVersion7(), OwnerId = erased, Title = "Erased route", Surface = RouteSurface.Mixed, RouteJson = json, Revision = 1, CreatedAt = now, UpdatedAt = now };
        var keptRoute = new LibraryRoute { Id = Guid.CreateVersion7(), OwnerId = kept, Title = "Preserved route", Surface = RouteSurface.Mixed, RouteJson = json, Revision = 1, CreatedAt = now, UpdatedAt = now };
        var collection = new BikeHub.Dal.Entities.RouteCollection { Id = Guid.CreateVersion7(), OwnerId = erased, Name = "Private collection", NormalizedName = "private collection", CreatedAt = now };
        db.LibraryRoutes.AddRange(route, keptRoute); db.RouteCollections.Add(collection);
        db.SavedRoutes.AddRange(new SavedRoute { RouteId = keptRoute.Id, UserId = erased, CollectionId = collection.Id, Note = "Private note", CreatedAt = now },
            new SavedRoute { RouteId = route.Id, UserId = kept, CreatedAt = now });
        db.RouteReviews.AddRange(new RouteReview { Id = Guid.CreateVersion7(), RouteId = keptRoute.Id, AuthorId = erased, Rating = 5, Text = "Erased review", RiddenOn = component.InstalledOn, CreatedAt = now, UpdatedAt = now },
            new RouteReview { Id = Guid.CreateVersion7(), RouteId = route.Id, AuthorId = kept, Rating = 4, Text = "Review on erased route", RiddenOn = component.InstalledOn, CreatedAt = now, UpdatedAt = now });
    }

}
