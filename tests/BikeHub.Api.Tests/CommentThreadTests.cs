using System.Net;
using System.Net.Http.Json;
using BikeHub.Api.Services;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Tests;

[Collection("Postgres persistence")]
public sealed class CommentThreadTests
{
    private static AuthFactory App(PersistenceTestDatabase database)
    {
        var app = new AuthFactory { Database = database };
        app.Settings["Authentication:Store"] = "Postgres";
        app.Settings["Community:Store"] = "Postgres";
        return app;
    }
    private static async Task<T> Create<T>(HttpClient client, string path, object body)
    {
        using var response = await AuthTestClient.Post(client, path, body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<PostDto> Post(HttpClient client)
    {
        var bike = await Create<BikeDto>(client, "/api/bikes", new { name = "Thread bicycle", brand = "Fixture", category = "gravel", year = 2026, mileage = 0, description = "Fixture" });
        return await Create<PostDto>(client, "/api/posts", new { bikeId = bike.Id, title = "Discussion post", content = "Проверяем ответы на комментарии", category = "story" });
    }

    [Fact]
    public async Task Replies_preserve_context_notify_parent_author_and_reject_invisible_or_foreign_parents()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var owner = app.Browser(); using var actor = app.Browser(); using var third = app.Browser(); using var guest = app.Browser();
        var (_, o) = await AuthTestClient.Login(owner, "thread-owner@example.test"); o.Dispose();
        var (a, ar) = await AuthTestClient.Login(actor, "thread-actor@example.test"); ar.Dispose();
        var (_, tr) = await AuthTestClient.Login(third, "thread-third@example.test"); tr.Dispose();
        var post = await Post(owner); var other = await Post(owner);
        var parent = await Create<CommentDto>(actor, $"/api/posts/{post.Id}/comments", new { text = "Исходный вопрос" });
        var reply = await Create<CommentDto>(third, $"/api/posts/{post.Id}/comments", new { text = "Ответ на вопрос", replyToId = parent.Id });
        Assert.Equal(parent.Id, reply.ReplyToId); Assert.Equal(parent.Text, reply.ReplyTo!.Text); Assert.Equal(a.User!.Handle, reply.ReplyTo.AuthorHandle);
        var inbox = (await actor.GetFromJsonAsync<Page<NotificationDto>>("/api/notifications"))!;
        var notification = Assert.Single(inbox.Items); Assert.Equal(NotificationKind.Reply, notification.Kind); Assert.Equal(reply.Id, notification.TargetId); Assert.Equal(post.Id, notification.ContainerId);
        Assert.Single((await owner.GetFromJsonAsync<Page<NotificationDto>>("/api/notifications"))!.Items);
        var direct = (await guest.GetFromJsonAsync<CommentDto>($"/api/posts/{post.Id}/comments/{reply.Id}"))!;
        Assert.Equal(parent.Id, direct.ReplyTo!.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/posts/{other.Id}/comments/{reply.Id}")).StatusCode);
        using var wrong = await AuthTestClient.Post(third, $"/api/posts/{other.Id}/comments", new { text = "Чужой контекст", replyToId = parent.Id });
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        using (var db = database.CreateDbContext())
        {
            await db.Comments.Where(c => c.Id == parent.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.IsHidden, true));
        }
        var redacted = (await guest.GetFromJsonAsync<CommentDto>($"/api/posts/{post.Id}/comments/{reply.Id}"))!;
        Assert.Equal(parent.Id, redacted.ReplyToId); Assert.Null(redacted.ReplyTo);
        using var hidden = await AuthTestClient.Post(third, $"/api/posts/{post.Id}/comments", new { text = "Скрытый контекст", replyToId = parent.Id });
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using (var db = database.CreateDbContext())
        {
            await db.Comments.Where(c => c.Id == parent.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.IsHidden, false));
            await db.Accounts.Where(u => u.Id == a.User.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.IsBlocked, true));
        }
        Assert.Null((await guest.GetFromJsonAsync<CommentDto>($"/api/posts/{post.Id}/comments/{reply.Id}"))!.ReplyTo);
        using (var db = database.CreateDbContext()) { await db.Comments.Where(c => c.Id == parent.Id).ExecuteDeleteAsync(); }
        var detached = (await guest.GetFromJsonAsync<CommentDto>($"/api/posts/{post.Id}/comments/{reply.Id}"))!;
        Assert.Null(detached.ReplyToId); Assert.Equal(reply.Text, detached.Text);
    }

    [Fact]
    public async Task Group_threads_enforce_membership_scope_and_source_visibility()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var owner = app.Browser(); using var member = app.Browser(); using var guest = app.Browser();
        var (_, o) = await AuthTestClient.Login(owner, "group-thread-owner@example.test"); o.Dispose();
        var (m, mr) = await AuthTestClient.Login(member, "group-thread-member@example.test"); mr.Dispose();
        var group = await Create<CommunityDto>(owner, "/api/communities", new { name = "Thread group", description = "Fixture", category = "gravel" });
        using (var db = database.CreateDbContext()) { db.Relations.Add(new CommunityRelation { UserId = m.User!.Id, TargetId = group.Id, Kind = RelationKind.Membership }); await db.SaveChangesAsync(); }
        var entry = await Create<GroupEntryDto>(owner, $"/api/communities/{group.Id}/entries", new { kind = 2, title = "Обсудим?", content = "Разговор", imageIds = Array.Empty<Guid>() });
        var another = await Create<GroupEntryDto>(owner, $"/api/communities/{group.Id}/entries", new { kind = 2, title = "Другое", content = "Разговор", imageIds = Array.Empty<Guid>() });
        var parent = await Create<CommentDto>(member, $"/api/group-entries/{entry.Id}/replies", new { text = "Вопрос участника" });
        var reply = await Create<CommentDto>(owner, $"/api/group-entries/{entry.Id}/replies", new { text = "Ответ организатора", replyToId = parent.Id });
        Assert.Equal(parent.Id, reply.ReplyToId); Assert.Equal(parent.Text, reply.ReplyTo!.Text);
        Assert.Equal(reply.Id, Assert.Single((await member.GetFromJsonAsync<Page<NotificationDto>>("/api/notifications"))!.Items).TargetId);
        Assert.Equal(reply.Id, (await guest.GetFromJsonAsync<CommentDto>($"/api/group-entries/{entry.Id}/replies/{reply.Id}"))!.Id);
        using var wrong = await AuthTestClient.Post(owner, $"/api/group-entries/{another.Id}/replies", new { text = "Другой тред", replyToId = parent.Id });
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        using (var db = database.CreateDbContext()) { await db.Relations.Where(r => r.UserId == m.User!.Id && r.Kind == RelationKind.Membership).ExecuteDeleteAsync(); }
        using var nonMember = await AuthTestClient.Post(member, $"/api/group-entries/{entry.Id}/replies", new { text = "Нет членства", replyToId = parent.Id });
        Assert.Equal(HttpStatusCode.Forbidden, nonMember.StatusCode);
        using (var db = database.CreateDbContext()) { await db.GroupReplies.Where(r => r.Id == parent.Id).ExecuteUpdateAsync(u => u.SetProperty(r => r.IsDeleted, true)); }
        Assert.Null((await guest.GetFromJsonAsync<CommentDto>($"/api/group-entries/{entry.Id}/replies/{reply.Id}"))!.ReplyTo);
        using (var db = database.CreateDbContext()) { await db.Communities.Where(g => g.Id == group.Id).ExecuteUpdateAsync(u => u.SetProperty(g => g.IsHidden, true)); }
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/group-entries/{entry.Id}/replies/{reply.Id}")).StatusCode);
    }
}
