using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using BikeHub.Api.Services;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BikeHub.Api.Tests;

[Collection("Postgres persistence")]
public sealed class SearchTests
{
    private static AuthFactory App(IDbContextFactory<BikeHubDbContext> database)
    {
        var app = new AuthFactory { Database = database };
        app.Settings["Authentication:Store"] = "Postgres"; app.Settings["Community:Store"] = "Postgres";
        return app;
    }
    private static async Task<(Guid Owner, Guid Bike, Guid Group)> Seed(PersistenceTestDatabase database)
    {
        await new DatabaseMigrator(database).MigrateAsync(default);
        using var db = database.CreateDbContext();
        var owner = new AuthAccountEntity { Id = Guid.CreateVersion7(), Email = "private-owner@example.test", DisplayName = "Маршрутный пользователь", Handle = "search_owner", City = "Казань", EmailConfirmedAt = DateTimeOffset.UtcNow };
        var bike = new Bike { Id = Guid.CreateVersion7(), OwnerId = owner.Id, Name = "Bicycle" };
        var group = new CommunityGroup { Id = Guid.CreateVersion7(), OwnerId = owner.Id, Name = "Маршрутная группа", Description = "Совместные поездки", Category = (int)BikeCategory.Gravel };
        db.Accounts.Add(owner); db.Bikes.Add(bike); db.Communities.Add(group); await db.SaveChangesAsync();
        return (owner.Id, bike.Id, group.Id);
    }
    private static CommunityPost Post(Guid owner, Guid bike, string title) => new()
    {
        Id = Guid.CreateVersion7(), AuthorId = owner, BikeId = bike, Title = title, Content = "История поездки", Category = "story", CreatedAt = DateTimeOffset.UnixEpoch
    };
    private static GroupEntry Entry(Guid owner, Guid group, string title) => new()
    {
        Id = Guid.CreateVersion7(), AuthorId = owner, GroupId = group, Title = title, Content = "Текст обсуждения", Kind = GroupEntryKind.Discussion, CreatedAt = DateTimeOffset.UnixEpoch
    };
    private static async Task<Page<T>?> Search<T>(HttpClient browser, string kind, string q, string paging = "")
    {
        using var response = await browser.GetAsync("/api/search/" + kind + "?q=" + Uri.EscapeDataString(q) + paging);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Page<T>>();
    }

    [Fact]
    public async Task Public_search_excludes_hidden_deleted_blocked_and_private_sources()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); var (owner, bike, group) = await Seed(database);
        using (var db = database.CreateDbContext())
        {
            var blocked = new AuthAccountEntity { Id = Guid.CreateVersion7(), Email = "blocked@example.test", DisplayName = "Маршрутный скрытый", Handle = "search_blocked", IsBlocked = true };
            var blockedBike = new Bike { Id = Guid.CreateVersion7(), OwnerId = blocked.Id, Name = "Blocked bicycle" };
            var hiddenBike = new Bike { Id = Guid.CreateVersion7(), OwnerId = owner, Name = "Hidden bicycle", IsHidden = true };
            var deletedBike = new Bike { Id = Guid.CreateVersion7(), OwnerId = owner, Name = "Deleted bicycle", IsDeleted = true };
            var hiddenGroup = new CommunityGroup { Id = Guid.CreateVersion7(), Name = "Маршрутная hidden group", IsHidden = true };
            var deletedGroup = new CommunityGroup { Id = Guid.CreateVersion7(), Name = "Маршрутная deleted group", IsDeleted = true };
            db.Accounts.Add(blocked); db.Bikes.AddRange(blockedBike, hiddenBike, deletedBike); db.Communities.AddRange(hiddenGroup, deletedGroup);
            var publicPost = Post(owner, bike, "Маршрут публичный");
            var hiddenPost = Post(owner, bike, "Маршрут скрытый"); hiddenPost.IsHidden = true;
            var deletedPost = Post(owner, bike, "Маршрут удалённый"); deletedPost.IsDeleted = true;
            db.Posts.AddRange(publicPost, hiddenPost, deletedPost, Post(owner, hiddenBike.Id, "Маршрут скрытого велосипеда"), Post(owner, deletedBike.Id, "Маршрут удалённого велосипеда"), Post(blocked.Id, blockedBike.Id, "Маршрут заблокированного"));
            var visible = Entry(owner, group, "Маршрут открытый"); visible.RouteJson = """{"name":"Route","segments":[],"markers":[{"lat":55,"lng":37,"label":"Start"}]}""";
            var hidden = Entry(owner, group, "Маршрут скрытый"); hidden.IsHidden = true;
            var deleted = Entry(owner, group, "Маршрут удалённый"); deleted.IsDeleted = true;
            db.GroupEntries.AddRange(visible, hidden, deleted, Entry(owner, hiddenGroup.Id, "Маршрут скрытой группы"), Entry(owner, deletedGroup.Id, "Маршрут удалённой группы"), Entry(blocked.Id, group, "Маршрут заблокированного"));
            db.Drafts.Add(new ContentDraft { OwnerId = owner, Scope = "post-new", Revision = 1, StateJson = """{"content":"PRIVATE_DRAFT_MARKER"}""", UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        await using var app = App(database); using var guest = app.Browser();
        Assert.Single((await Search<PostDto>(guest, "posts", "мАРШРут"))!.Items);
        Assert.Single((await Search<CommunityDto>(guest, "communities", "маршрутная"))!.Items);
        Assert.Single((await Search<UserDto>(guest, "people", "маршрут"))!.Items);
        var discussions = (await Search<GroupEntryDto>(guest, "discussions", "маршрут"))!; Assert.Single(discussions.Items);
        Assert.True(discussions.Items[0].HasRoute); Assert.Null(discussions.Items[0].Route);
        Assert.Empty((await Search<PostDto>(guest, "posts", "PRIVATE_DRAFT_MARKER"))!.Items);
        using var response = await guest.GetAsync("/api/search/people?q=" + Uri.EscapeDataString("КАЗАНЬ"));
        Assert.True(response.Headers.CacheControl!.NoStore);
        var body = await response.Content.ReadAsStringAsync(); Assert.Contains("search_owner", body); Assert.DoesNotContain("private-owner@example.test", body);
        using var block = database.CreateDbContext(); await block.Accounts.Where(a => a.Id == owner).ExecuteUpdateAsync(u => u.SetProperty(a => a.IsBlocked, true));
        Assert.Empty((await Search<PostDto>(guest, "posts", "маршрут"))!.Items);
        Assert.Empty((await Search<GroupEntryDto>(guest, "discussions", "маршрут"))!.Items);
        Assert.Empty((await Search<UserDto>(guest, "people", "маршрут"))!.Items);
    }

    [Fact]
    public async Task Search_escapes_wildcards_supports_nicknames_and_pages_stably_with_bounded_inputs()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); var (owner, bike, group) = await Seed(database);
        using (var db = database.CreateDbContext())
        {
            db.Posts.AddRange(Post(owner, bike, "100%_готов \\дорога"), Post(owner, bike, "100xxXготов дорога"));
            for (var i = 0; i < 5; i++) { db.Posts.Add(Post(owner, bike, "Пагинация " + i)); }
            db.GroupEntries.Add(Entry(owner, group, "100%_готов \\дорога"));
            var other = new CommunityGroup { Id = Guid.CreateVersion7(), Name = "Other group" }; db.Communities.Add(other);
            db.GroupEntries.Add(Entry(owner, other.Id, "100%_готов \\дорога")); await db.SaveChangesAsync();
        }
        await using var app = App(database); using var guest = app.Browser();
        foreach (var term in new[] { "%", "_", "\\", "100%_готов" }) { Assert.True((await Search<PostDto>(guest, "posts", term))!.Items.Count == 1, "Failed literal term: " + term); }
        Assert.Single((await Search<UserDto>(guest, "people", "@SEARCH_OWNER"))!.Items);
        var first = (await Search<PostDto>(guest, "posts", "Пагинация", "&pageSize=2"))!;
        var second = (await Search<PostDto>(guest, "posts", "Пагинация", "&pageSize=2&page=2"))!;
        Assert.Equal(5, first.Total); Assert.True(first.HasMore); Assert.Equal(2, first.Items.Count);
        Assert.Empty(first.Items.Select(p => p.Id).Intersect(second.Items.Select(p => p.Id)));
        Assert.Equal(first.Items.Select(p => p.Id), (await Search<PostDto>(guest, "posts", "Пагинация", "&pageSize=2"))!.Items.Select(p => p.Id));
        var scoped = await guest.GetFromJsonAsync<Page<GroupEntryDto>>($"/api/communities/{group}/entries?kind=2&q=" + Uri.EscapeDataString("100%_готов")); Assert.Single(scoped!.Items);
        foreach (var kind in new[] { "posts", "people", "communities", "discussions" })
        {
            using var longQuery = await guest.GetAsync("/api/search/" + kind + "?q=" + new string('@', 201)); Assert.Equal(HttpStatusCode.BadRequest, longQuery.StatusCode);
            using var tooLarge = await guest.GetAsync("/api/search/" + kind + "?q=test&pageSize=51"); Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
            using var negative = await guest.GetAsync("/api/search/" + kind + "?q=test&page=0"); Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        }
        Assert.Empty((await Search<PostDto>(guest, "posts", " "))!.Items);
        Assert.Empty((await Search<PostDto>(guest, "posts", "Пагинация", "&page=1000000"))!.Items);
    }

    private sealed class Queries : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Commands.Enqueue(command.CommandText); return result;
        }
    }
    private sealed class MeasuredFactory(string connection, Queries queries) : IDbContextFactory<BikeHubDbContext>
    {
        public BikeHubDbContext CreateDbContext() => new(new DbContextOptionsBuilder<BikeHubDbContext>().UseNpgsql(connection).AddInterceptors(queries).Options);
    }
    [Fact]
    public async Task Feed_and_comment_pages_keep_query_counts_bounded_and_omit_route_bodies()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); var (owner, bike, _) = await Seed(database);
        using var db = database.CreateDbContext(); var first = Post(owner, bike, "Initial ride"); db.Posts.Add(first);
        for (var i = 0; i < 55; i++)
        {
            var author = new AuthAccountEntity { Id = Guid.CreateVersion7(), Email = $"paging_{i}@example.test", DisplayName = "Rider " + i, Handle = "paging_" + i };
            var bicycle = new Bike { Id = Guid.CreateVersion7(), OwnerId = author.Id, Name = "Bicycle " + i };
            var post = Post(author.Id, bicycle.Id, "Route " + i);
            post.RouteJson = """{"name":"Route body must stay out of feeds","segments":[[{"lat":55,"lng":37},{"lat":56,"lng":38}]],"markers":[]}""";
            db.Accounts.Add(author); db.Bikes.Add(bicycle); db.Posts.Add(post);
            db.Comments.Add(new CommunityComment { Id = Guid.CreateVersion7(), PostId = first.Id, AuthorId = author.Id, Text = "Comment " + i, CreatedAt = DateTimeOffset.UnixEpoch });
        }
        await db.SaveChangesAsync();
        var queries = new Queries(); await using var app = App(new MeasuredFactory(db.Database.GetConnectionString()!, queries)); using var guest = app.Browser();
        foreach (var size in new[] { 1, 50 })
        {
            queries.Commands.Clear();
            var feed = (await guest.GetFromJsonAsync<Page<PostDto>>($"/api/feed?pageSize={size}"))!;
            Assert.Equal(size, feed.Items.Count); Assert.Equal(56, feed.Total); Assert.True(feed.HasMore);
            Assert.Equal(2, queries.Commands.Count);
            Assert.All(feed.Items, post => { Assert.True(post.HasRoute); Assert.Null(post.Route); });
            queries.Commands.Clear();
            var comments = (await guest.GetFromJsonAsync<Page<CommentDto>>($"/api/posts/{first.Id}/comments?pageSize={size}"))!;
            Assert.Equal(size, comments.Items.Count); Assert.Equal(55, comments.Total); Assert.True(comments.HasMore);
            Assert.Equal(3, queries.Commands.Count);
        }
    }
    [Fact]
    public async Task Discussion_replies_batch_authors_and_keep_stable_private_filtered_paging()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); var (owner, _, group) = await Seed(database);
        using var db = database.CreateDbContext(); var entry = Entry(owner, group, "Discussion"); db.GroupEntries.Add(entry);
        var blocked = new AuthAccountEntity { Id = Guid.CreateVersion7(), Email = "blocked-replies@example.test", DisplayName = "Blocked", Handle = "blocked_replies", IsBlocked = true }; db.Accounts.Add(blocked);
        for (var i = 0; i < 55; i++) { db.GroupReplies.Add(new GroupReply { Id = Guid.CreateVersion7(), AuthorId = owner, EntryId = entry.Id, Text = "Reply " + i, CreatedAt = DateTimeOffset.UnixEpoch }); }
        db.GroupReplies.Add(new GroupReply { Id = Guid.CreateVersion7(), AuthorId = blocked.Id, EntryId = entry.Id, Text = "Blocked private text" });
        db.GroupReplies.Add(new GroupReply { Id = Guid.CreateVersion7(), AuthorId = owner, EntryId = entry.Id, Text = "Hidden private text", IsHidden = true });
        await db.SaveChangesAsync();
        var queries = new Queries(); await using var app = App(new MeasuredFactory(db.Database.GetConnectionString()!, queries)); using var guest = app.Browser();
        var first = (await guest.GetFromJsonAsync<Page<CommentDto>>($"/api/group-entries/{entry.Id}/replies"))!;
        Assert.Equal(50, first.Items.Count); Assert.Equal(55, first.Total); Assert.True(first.HasMore); Assert.Equal(3, queries.Commands.Count);
        var second = (await guest.GetFromJsonAsync<Page<CommentDto>>($"/api/group-entries/{entry.Id}/replies?page=2"))!;
        Assert.Equal(5, second.Items.Count); Assert.False(second.HasMore); Assert.Empty(first.Items.Select(r => r.Id).Intersect(second.Items.Select(r => r.Id)));
        Assert.DoesNotContain(first.Items.Concat(second.Items), r => r.Text.Contains("private", StringComparison.Ordinal));
        await db.Communities.Where(g => g.Id == group).ExecuteUpdateAsync(g => g.SetProperty(x => x.IsHidden, true));
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/group-entries/{entry.Id}/replies")).StatusCode);
    }
    [Fact]
    public async Task Community_catalog_is_bounded_and_late_entries_remain_accessible_directly()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); var (owner, _, _) = await Seed(database);
        using var db = database.CreateDbContext(); var ids = new List<Guid>();
        for (var i = 0; i < 105; i++)
        {
            var id = Guid.CreateVersion7(); ids.Add(id);
            db.Communities.Add(new CommunityGroup { Id = id, OwnerId = owner, Name = "Catalog group " + i.ToString("D3"), Category = (int)BikeCategory.Gravel });
        }
        db.Communities.Add(new CommunityGroup { Id = Guid.CreateVersion7(), Name = "Catalog group hidden", IsHidden = true });
        db.Communities.Add(new CommunityGroup { Id = Guid.CreateVersion7(), Name = "Catalog group deleted", IsDeleted = true }); await db.SaveChangesAsync();
        await using var app = App(database); using var guest = app.Browser();
        using var firstResponse = await guest.GetAsync("/api/communities/catalog?q=Catalog%20group&pageSize=20");
        Assert.True(firstResponse.Headers.CacheControl!.NoStore);
        var first = (await firstResponse.Content.ReadFromJsonAsync<Page<CommunityDto>>())!; Assert.Equal(105, first.Total); Assert.Equal(20, first.Items.Count); Assert.True(first.HasMore);
        var second = (await guest.GetFromJsonAsync<Page<CommunityDto>>("/api/communities/catalog?q=Catalog%20group&pageSize=20&page=2"))!;
        Assert.Empty(first.Items.Select(g => g.Id).Intersect(second.Items.Select(g => g.Id)));
        var legacy = (await guest.GetFromJsonAsync<CommunityDto[]>("/api/communities?q=Catalog%20group"))!; Assert.Equal(100, legacy.Length);
        Assert.DoesNotContain(legacy, g => g.Id == ids[^1]);
        Assert.Equal(ids[^1], (await guest.GetFromJsonAsync<CommunityDto>($"/api/communities/{ids[^1]}"))!.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync("/api/communities/catalog?pageSize=51")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync("/api/communities/catalog?q=" + new string('a', 201))).StatusCode);
    }
    [Fact]
    public async Task Group_search_loads_authors_in_a_batch_and_its_indexes_are_available_to_the_planner()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); var (owner, _, group) = await Seed(database);
        using var db = database.CreateDbContext();
        for (var i = 0; i < 25; i++) { db.GroupEntries.Add(Entry(owner, group, "Гравий " + i)); }
        for (var i = 0; i < 5000; i++) { db.GroupEntries.Add(Entry(owner, group, "Другая тема " + i)); }
        await db.SaveChangesAsync();
        var queries = new Queries(); await using var app = App(new MeasuredFactory(db.Database.GetConnectionString()!, queries)); using var guest = app.Browser();
        var page = await guest.GetFromJsonAsync<Page<GroupEntryDto>>($"/api/communities/{group}/entries?kind=2&q=" + Uri.EscapeDataString("гравий"));
        Assert.Equal(20, page!.Items.Count); Assert.Equal(25, page.Total); Assert.True(page.HasMore);
        Assert.Equal(4, queries.Commands.Count);
        await using var connection = db.Database.GetDbConnection(); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "VACUUM ANALYZE group_entries"; await command.ExecuteNonQueryAsync();
        command.CommandText = "EXPLAIN SELECT id FROM group_entries WHERE NOT is_deleted AND NOT is_hidden AND (title || ' ' || content) ILIKE '%Гравий%'";
        await using var plan = await command.ExecuteReaderAsync(); var lines = new List<string>(); while (await plan.ReadAsync()) { lines.Add(plan.GetString(0)); }
        Assert.Contains(lines, line => line.Contains("ix_group_entries_search", StringComparison.Ordinal));
    }
}
