using System.Net;
using System.Net.Http.Json;
using BikeHub.Api.Services;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Tests;

[Collection("Postgres persistence")]
public sealed class ActivityFeedTests
{
    [Fact]
    public async Task Timeline_unifies_sources_and_memberships_with_stable_paging_and_visibility()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = new AuthFactory { Database = database };
        app.Settings["Authentication:Store"] = "Postgres";
        app.Settings["Community:Store"] = "Postgres";
        using var viewer = app.Browser(); using var guest = app.Browser();
        var (session, login) = await AuthTestClient.Login(viewer, "feed-viewer@example.test"); login.Dispose();
        var owner = new AuthAccountEntity { Id = Guid.CreateVersion7(), Email = "feed-author@example.test", DisplayName = "Автор ленты", Handle = "feed_author" };
        var bike = new Bike { Id = Guid.CreateVersion7(), OwnerId = owner.Id, Name = "Гравий", Category = (int)BikeCategory.Gravel };
        var group = new CommunityGroup { Id = Guid.CreateVersion7(), OwnerId = owner.Id, Name = "Маршрутная группа", Category = (int)BikeCategory.Road };
        var other = new CommunityGroup { Id = Guid.CreateVersion7(), Name = "Другая группа" };
        var id = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow;
        using (var db = database.CreateDbContext())
        {
            db.Accounts.Add(owner); db.Bikes.Add(bike); db.Communities.AddRange(group, other);
            db.Posts.Add(new CommunityPost { Id = id, AuthorId = owner.Id, BikeId = bike.Id, Title = "Пост 100%_", Content = "Текст", Category = "ride", CreatedAt = now,
                RouteJson = "{\"name\":\"Private large body\",\"segments\":[],\"markers\":[]}" });
            db.GroupEntries.AddRange(
                new GroupEntry { Id = id, AuthorId = owner.Id, GroupId = group.Id, Kind = GroupEntryKind.Discussion, Title = "Обсуждение 100%_", Content = "Текст", CreatedAt = now },
                new GroupEntry { Id = Guid.CreateVersion7(), AuthorId = owner.Id, GroupId = other.Id, Kind = GroupEntryKind.Post, Title = "Другой пост", Content = "Текст", CreatedAt = now.AddMinutes(-1) },
                new GroupEntry { Id = Guid.CreateVersion7(), AuthorId = owner.Id, GroupId = group.Id, Kind = GroupEntryKind.Post, Title = "Скрытый", IsHidden = true },
                new GroupEntry { Id = Guid.CreateVersion7(), AuthorId = owner.Id, GroupId = group.Id, Kind = GroupEntryKind.Post, Title = "Удалённый", IsDeleted = true });
            db.Relations.Add(new CommunityRelation { UserId = session.User!.Id, Kind = RelationKind.Membership, TargetId = group.Id });
            await db.SaveChangesAsync();
        }
        async Task<Page<FeedItemDto>> Feed(HttpClient client, string query = "")
        {
            using var response = await client.GetAsync("/api/feed/timeline?" + query);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.True(response.Headers.CacheControl!.NoStore);
            return (await response.Content.ReadFromJsonAsync<Page<FeedItemDto>>())!;
        }
        var all = await Feed(guest);
        Assert.Equal(3, all.Total); Assert.Equal(3, all.Items.Count);
        Assert.Equal(3, all.Items.Select(x => x.Id).Distinct().Count());
        Assert.Equal(FeedItemKind.Post, all.Items[0].Kind); Assert.Equal(FeedItemKind.GroupEntry, all.Items[1].Kind);
        Assert.True(all.Items[0].Post!.HasRoute); Assert.Null(all.Items[0].Post!.Route);
        Assert.Single((await Feed(viewer, "tab=following")).Items);
        using (var db = database.CreateDbContext())
        {
            db.Relations.Add(new CommunityRelation { UserId = session.User!.Id, Kind = RelationKind.Follow, TargetId = owner.Id }); await db.SaveChangesAsync();
        }
        Assert.Equal(2, (await Feed(viewer, "tab=following")).Total);
        var first = await Feed(guest, "pageSize=1"); var second = await Feed(guest, "pageSize=1&page=2");
        Assert.True(first.HasMore); Assert.NotEqual(first.Items[0].Id, second.Items[0].Id);
        Assert.Equal(first.Items[0].Id, (await Feed(guest, "pageSize=1")).Items[0].Id);
        Assert.Equal(2, (await Feed(guest, "q=" + Uri.EscapeDataString("100%_"))).Total);
        Assert.Single((await Feed(guest, "category=road")).Items);
        Assert.Single((await Feed(guest, "category=gravel")).Items);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/feed/timeline?tab=following")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync("/api/feed/timeline?pageSize=51")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync("/api/feed/timeline?q=" + new string('a', 201))).StatusCode);
        using (var db = database.CreateDbContext())
        {
            await db.Communities.Where(g => g.Id == group.Id).ExecuteUpdateAsync(u => u.SetProperty(g => g.IsHidden, true));
        }
        Assert.Equal(2, (await Feed(guest)).Total);
        using (var db = database.CreateDbContext())
        {
            await db.Accounts.Where(a => a.Id == owner.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.IsBlocked, true));
        }
        Assert.Empty((await Feed(guest)).Items);
        Assert.Empty((await Feed(viewer, "tab=following")).Items);
    }
}
