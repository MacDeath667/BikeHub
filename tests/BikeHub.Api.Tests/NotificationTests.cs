using System.Net;
using System.Net.Http.Json;
using BikeHub.Api.Authentication;
using BikeHub.Api.Services;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Tests;

[Collection("Postgres persistence")]
public sealed class NotificationTests
{
    private static AuthFactory App(PersistenceTestDatabase database)
    {
        var app = new AuthFactory { Database = database };
        app.Settings["Authentication:Store"] = "Postgres"; app.Settings["Community:Store"] = "Postgres";
        app.Settings["Moderation:ModeratorEmails:0"] = "owner@example.test";
        return app;
    }
    private static async Task Set(HttpClient browser, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.NoContent)
    {
        var session = await browser.GetFromJsonAsync<AuthBootstrap>("/api/auth/session");
        using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("X-CSRF-TOKEN", session!.CsrfToken);
        using var result = await browser.SendAsync(request);
        Assert.Equal(expected, result.StatusCode);
    }
    private static async Task<T> Create<T>(HttpClient browser, string path, object body)
    {
        using var result = await AuthTestClient.Post(browser, path, body);
        Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync());
        return (await result.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<PostDto> Post(HttpClient browser, string content = "Проверочная запись для уведомлений")
    {
        var bike = await Create<BikeDto>(browser, "/api/bikes", new { name = "Notification bicycle", brand = "Fixture", category = "gravel", year = 2026, mileage = 0, description = "Fixture" });
        return await Create<PostDto>(browser, "/api/posts", new { bikeId = bike.Id, title = "Notification test", content, category = "story" });
    }

    [Fact]
    public async Task Comments_mentions_and_follows_are_private_deduplicated_readable_and_persistent()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var owner = app.Browser(); using var actor = app.Browser(); using var tagged = app.Browser(); using var guest = app.Browser();
        var (o, or) = await AuthTestClient.Login(owner, "owner@example.test"); or.Dispose();
        var (a, ar) = await AuthTestClient.Login(actor, "actor@example.test"); ar.Dispose();
        var (t, tr) = await AuthTestClient.Login(tagged, "tagged@example.test"); tr.Dispose();
        var post = await Post(owner);
        await Create<CommentDto>(owner, $"/api/posts/{post.Id}/comments", new { text = "Сам себе отвечаю" });
        Assert.Equal(0, await owner.GetFromJsonAsync<int>("/api/notifications/unread-count"));
        var comment = await Create<CommentDto>(actor, $"/api/posts/{post.Id}/comments", new { text = $"Привет @{o.User!.Handle} и @{t.User!.Handle}!" });
        var inbox = (await owner.GetFromJsonAsync<Page<NotificationDto>>("/api/notifications"))!;
        Assert.Equal(NotificationKind.Comment, Assert.Single(inbox.Items).Kind);
        Assert.Equal(a.User!.Id, inbox.Items[0].ActorId);
        Assert.Equal(comment.Id, inbox.Items[0].TargetId);
        Assert.Equal(NotificationKind.Mention, Assert.Single((await tagged.GetFromJsonAsync<Page<NotificationDto>>("/api/notifications"))!.Items).Kind);
        Assert.Equal(0, await actor.GetFromJsonAsync<int>("/api/notifications/unread-count"));
        await Set(actor, $"/api/posts/{post.Id}/comments/{comment.Id}", new { text = $"Ещё раз @{t.User.Handle}" }, HttpStatusCode.OK);
        Assert.Equal(1, await tagged.GetFromJsonAsync<int>("/api/notifications/unread-count"));
        await Set(actor, $"/api/users/{o.User.Id}/follow", expected: HttpStatusCode.OK);
        await Set(actor, $"/api/users/{o.User.Id}/follow", expected: HttpStatusCode.OK);
        Assert.Equal(2, await owner.GetFromJsonAsync<int>("/api/notifications/unread-count"));
        await Set(tagged, $"/api/notifications/{inbox.Items[0].Id}/read", expected: HttpStatusCode.NotFound);
        using var missingCsrf = await owner.PutAsJsonAsync($"/api/notifications/{inbox.Items[0].Id}/read", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/notifications")).StatusCode);
        await Set(owner, $"/api/notifications/{inbox.Items[0].Id}/read");
        await using (var db = await database.CreateDbContextAsync())
        {
            var readAt = (await db.Notifications.SingleAsync(n => n.Id == inbox.Items[0].Id)).ReadAt;
            app.Clock.Advance(TimeSpan.FromMinutes(1));
            await Set(owner, $"/api/notifications/{inbox.Items[0].Id}/read");
            Assert.Equal(readAt, (await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == inbox.Items[0].Id)).ReadAt);
        }
        await Set(owner, "/api/notifications/read");
        Assert.Equal(0, await owner.GetFromJsonAsync<int>("/api/notifications/unread-count"));
        Assert.DoesNotContain("@example.test", await owner.GetStringAsync("/api/notifications"));
        await using var restarted = App(database); restarted.Clock.Advance(TimeSpan.FromMinutes(3)); using var again = restarted.Browser();
        var (_, signedIn) = await AuthTestClient.Login(again, "owner@example.test", registering: false); signedIn.Dispose();
        var persisted = (await again.GetFromJsonAsync<Page<NotificationDto>>("/api/notifications"))!;
        Assert.Equal(2, persisted.Total); Assert.All(persisted.Items, item => Assert.True(item.IsRead));
    }

    [Fact]
    public async Task Group_mentions_and_replies_follow_visibility_of_the_source_and_actor()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var owner = app.Browser(); using var actor = app.Browser();
        var (o, or) = await AuthTestClient.Login(owner, "owner@example.test"); or.Dispose();
        var (a, ar) = await AuthTestClient.Login(actor, "actor@example.test"); ar.Dispose();
        var group = await Create<CommunityDto>(owner, "/api/communities", new { name = "Notification group", description = "Fixture", category = "gravel" });
        await Set(actor, $"/api/communities/{group.Id}/membership", expected: HttpStatusCode.OK);
        var entry = await Create<GroupEntryDto>(owner, $"/api/communities/{group.Id}/entries", new { kind = 2, title = "Что обсудим?", content = $"Привет @{a.User!.Handle}", imageIds = Array.Empty<Guid>() });
        Assert.Equal(1, await actor.GetFromJsonAsync<int>("/api/notifications/unread-count"));
        await Create<CommentDto>(actor, $"/api/group-entries/{entry.Id}/replies", new { text = $"Ответ @{o.User!.Handle}" });
        Assert.Equal(NotificationKind.Reply, Assert.Single((await owner.GetFromJsonAsync<Page<NotificationDto>>("/api/notifications"))!.Items).Kind);
        await Set(owner, $"/api/moderation/7/{entry.Id}/visibility", new { hidden = true });
        Assert.Equal(0, await actor.GetFromJsonAsync<int>("/api/notifications/unread-count"));
        Assert.Equal(0, await owner.GetFromJsonAsync<int>("/api/notifications/unread-count"));
        await Set(owner, $"/api/moderation/7/{entry.Id}/visibility", new { hidden = false });
        await Set(owner, $"/api/moderation/users/{a.User.Id}/block", new { blocked = true });
        Assert.Empty((await owner.GetFromJsonAsync<Page<NotificationDto>>("/api/notifications"))!.Items);
    }

    [Fact]
    public async Task Mentions_in_posts_ignore_email_addresses_duplicates_self_and_invalid_names()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var owner = app.Browser(); using var actor = app.Browser();
        var (o, or) = await AuthTestClient.Login(owner, "owner@example.test"); or.Dispose();
        var (a, ar) = await AuthTestClient.Login(actor, "actor@example.test"); ar.Dispose();
        await Post(actor, $"Mail x@{o.User!.Handle}.test сам @{a.User!.Handle} неизвестный @not_registered");
        Assert.Equal(0, await owner.GetFromJsonAsync<int>("/api/notifications/unread-count"));
        var post = await Post(actor, $"Упоминание @{o.User.Handle.ToUpperInvariant()} и @{o.User.Handle}.");
        Assert.Equal(1, await owner.GetFromJsonAsync<int>("/api/notifications/unread-count"));
        await Set(actor, $"/api/posts/{post.Id}", new { bikeId = post.Bike.Id, title = post.Title, content = $"Снова @{o.User.Handle}", category = "story" }, HttpStatusCode.OK);
        Assert.Equal(1, await owner.GetFromJsonAsync<int>("/api/notifications/unread-count"));
        await Set(owner, $"/api/moderation/2/{post.Id}/visibility", new { hidden = true });
        Assert.Equal(0, await owner.GetFromJsonAsync<int>("/api/notifications/unread-count"));
    }
}
