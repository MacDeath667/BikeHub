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
public sealed class PostDeletionTests
{
    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        var session = (await client.GetFromJsonAsync<AuthBootstrap>("/api/auth/session"))!;
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        request.Headers.Add("X-CSRF-TOKEN", session.CsrfToken);
        return await client.SendAsync(request);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_author_or_current_admin_can_delete_a_post_including_hidden_posts(bool hidden)
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        using var app = new AuthFactory { Database = database };
        app.Settings["Community:Store"] = "Postgres";
        app.Settings["Authentication:Store"] = "Postgres";
        app.Settings["Moderation:ModeratorEmails:0"] = "moderator@example.test";
        using var owner = app.Browser();
        using var administrator = app.Browser();
        using var moderator = app.Browser();
        using var guest = app.Browser();
        var (author, ownerLogin) = await AuthTestClient.Login(owner, "author@example.test");
        ownerLogin.Dispose();
        var (futureAdmin, adminLogin) = await AuthTestClient.Login(administrator, "admin@example.test");
        adminLogin.Dispose();
        (await AuthTestClient.Login(moderator, "moderator@example.test")).Response.Dispose();

        using var createdBike = await Send(owner, HttpMethod.Post, "/api/bikes", new
        { name = "Author bike", brand = "Brand", category = "gravel", year = 2026, mileage = 100, description = "Bike description" });
        Assert.Equal(HttpStatusCode.Created, createdBike.StatusCode);
        var bike = (await createdBike.Content.ReadFromJsonAsync<BikeDto>())!;
        var postBody = new { bikeId = bike.Id, title = "Review this post", content = "A sufficiently long post for review", category = "ride" };
        async Task<PostDto> CreatePost()
        {
            using var created = await Send(owner, HttpMethod.Post, "/api/posts", postBody);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            return (await created.Content.ReadFromJsonAsync<PostDto>())!;
        }
        var post = await CreatePost();
        var otherPost = await CreatePost();
        using var createdComment = await Send(owner, HttpMethod.Post, $"/api/posts/{post.Id}/comments", new { text = "A comment under the post" });
        Assert.Equal(HttpStatusCode.Created, createdComment.StatusCode);
        var comment = (await createdComment.Content.ReadFromJsonAsync<CommentDto>())!;
        var photoId = Guid.NewGuid();
        await using (var db = database.CreateDbContext())
        {
            db.Media.Add(new MediaAsset { Id = photoId, OwnerId = author.User!.Id, CreatedAt = app.Clock.GetUtcNow() });
            await db.SaveChangesAsync();
            // The same photo is also attached to another post and must remain available there.
            await db.Posts.ExecuteUpdateAsync(u => u.SetProperty(p => p.ImageId, photoId).SetProperty(p => p.ImageIds, new[] { photoId }));
            await db.Posts.Where(p => p.Id == post.Id).ExecuteUpdateAsync(u => u.SetProperty(p => p.IsHidden, hidden));
        }

        var path = $"/api/posts/{post.Id}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(administrator, HttpMethod.Delete, path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(moderator, HttpMethod.Delete, path)).StatusCode);
        await AdminAccountCommand.GrantAsync(database, "admin@example.test", default);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(administrator, HttpMethod.Put, path, postBody)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(administrator, HttpMethod.Delete, $"/api/bikes/{bike.Id}")).StatusCode);

        await using (var db = database.CreateDbContext())
        {
            await db.Accounts.Where(a => a.Id == futureAdmin.User!.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.Role, AccountRole.Member));
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(administrator, HttpMethod.Delete, path)).StatusCode);
        await AdminAccountCommand.GrantAsync(database, "admin@example.test", default);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(administrator, HttpMethod.Delete, path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(path + "/comments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(path + $"/comments/{comment.Id}")).StatusCode);
        foreach (var endpoint in new[] { "/api/feed", "/api/search/posts?q=Review", $"/api/bikes/{bike.Id}/history" })
        {
            var remaining = (await guest.GetFromJsonAsync<Page<PostDto>>(endpoint))!;
            Assert.Equal(otherPost.Id, Assert.Single(remaining.Items).Id);
        }
        var timeline = (await guest.GetFromJsonAsync<Page<FeedItemDto>>("/api/feed/timeline"))!;
        Assert.Equal(otherPost.Id, Assert.Single(timeline.Items).Post!.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(administrator, HttpMethod.Delete, path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(moderator, HttpMethod.Put, $"/api/moderation/2/{post.Id}/visibility", new { hidden = false })).StatusCode);

        await using (var db = database.CreateDbContext())
        {
            var removed = await db.Posts.SingleAsync(p => p.Id == post.Id);
            Assert.True(removed.IsDeleted);
            Assert.Null(removed.ImageId);
            Assert.Empty(removed.ImageIds);
            Assert.False((await db.Bikes.SingleAsync(b => b.Id == bike.Id)).IsDeleted);
            Assert.False((await db.Accounts.SingleAsync(a => a.Id == author.User!.Id)).IsBlocked);
            Assert.False((await db.Media.SingleAsync(m => m.Id == photoId)).IsDeleted);
            var kept = await db.Posts.SingleAsync(p => p.Id == otherPost.Id);
            Assert.False(kept.IsDeleted);
            Assert.Equal(photoId, Assert.Single(kept.ImageIds));
        }
        Assert.Equal(HttpStatusCode.NoContent, (await Send(owner, HttpMethod.Delete, $"/api/posts/{otherPost.Id}")).StatusCode);
    }
}
