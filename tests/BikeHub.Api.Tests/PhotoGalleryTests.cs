using System.Net;
using System.Net.Http.Json;
using BikeHub.Api.Authentication;
using BikeHub.Api.Services;
using BikeHub.Application.Community;
using BikeHub.Dal;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BikeHub.Api.Tests;

[Collection("Postgres persistence")]
public sealed class PhotoGalleryTests
{
    private static async Task<HttpResponseMessage> Send(HttpClient browser, HttpMethod method, string path, object body)
    {
        var session = await browser.GetFromJsonAsync<AuthBootstrap>("/api/auth/session");
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", session!.CsrfToken);
        return await browser.SendAsync(request);
    }

    private static async Task<MediaDto> Upload(HttpClient browser)
    {
        using var image = new MagickImage(MagickColors.Green, 100, 80);
        var session = await browser.GetFromJsonAsync<AuthBootstrap>("/api/auth/session");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(image.ToByteArray(MagickFormat.Png)), "file", "photo.png");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/media") { Content = form };
        request.Headers.Add("X-CSRF-TOKEN", session!.CsrfToken);
        using var response = await browser.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MediaDto>())!;
    }

    [Fact]
    public async Task Migration_adopts_existing_covers_and_can_be_replayed_without_overwriting_a_gallery()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        await context.GetService<IMigrator>().MigrateAsync("20261110184844_EmailDeliveryOutbox");
        var owner = Guid.NewGuid();
        var photo = Guid.NewGuid();
        var bike = Guid.NewGuid();
        var post = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO auth_accounts (id,email,display_name,email_confirmed_at)
            VALUES ({owner}, {"cover@example.test"}, {"Existing rider"}, now());
            INSERT INTO media_assets (id,owner_id,width,height,bytes,created_at,is_deleted)
            VALUES ({photo}, {owner}, 100, 80, 1000, now(), false);
            INSERT INTO bikes (id,owner_id,name,image_id) VALUES ({bike},{owner},{"Existing bike"},{photo});
            INSERT INTO posts (id,bike_id,author_id,title,content,category,image_id,created_at,is_deleted,is_hidden)
            VALUES ({post},{bike},{owner},{"Existing post"},{"Existing post content"},{"ride"},{photo},now(),false,false);
            """);
        await new DatabaseMigrator(database).MigrateAsync(default);
        Assert.Equal(new[] { photo }, (await context.Bikes.SingleAsync()).ImageIds);
        Assert.Equal(new[] { photo }, (await context.Posts.SingleAsync()).ImageIds);
        // Replaying raw SQL must retain a gallery edited after its first application.
        var additional = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO media_assets (id,owner_id,width,height,bytes,created_at,is_deleted)
            VALUES ({additional},{owner},100,80,1000,now(),false);
            UPDATE bikes SET image_ids = {new[] { additional, photo }}, image_id = {additional} WHERE id = {bike};
            UPDATE posts SET image_ids = {new[] { additional, photo }}, image_id = {additional} WHERE id = {post};
            """);
        await context.Database.ExecuteSqlRawAsync("DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261110195505_ContentPhotoGalleries'");
        await new DatabaseMigrator(database).MigrateAsync(default);
        context.ChangeTracker.Clear();
        Assert.Equal(new[] { additional, photo }, (await context.Bikes.SingleAsync()).ImageIds);
        Assert.Equal(new[] { additional, photo }, (await context.Posts.SingleAsync()).ImageIds);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Bike_and_post_galleries_preserve_order_ownership_cover_and_visibility()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        var root = Path.Combine(Path.GetTempPath(), "bikehub-gallery-" + Guid.NewGuid());
        try
        {
            await using var app = new AuthFactory { Database = database };
            app.Settings["Authentication:Store"] = "Postgres";
            app.Settings["Community:Store"] = "Postgres";
            app.Settings["Media:RootPath"] = root;
            using var owner = app.Browser();
            using var foreign = app.Browser();
            using var anonymous = app.Browser();
            (await AuthTestClient.Login(owner, "gallery@example.test")).Response.Dispose();
            (await AuthTestClient.Login(foreign, "foreign@example.test")).Response.Dispose();
            var first = await Upload(owner);
            var second = await Upload(owner);
            var third = await Upload(owner);
            var foreignPhoto = await Upload(foreign);
            object BikeBody(Guid[] ids) => new { name = "Gallery bike", brand = "Brand", category = "gravel", year = 2026,
                mileage = 0, description = "Bike with photos", imageIds = ids };
            using var stolen = await Send(owner, HttpMethod.Post, "/api/bikes", BikeBody([first.Id, foreignPhoto.Id]));
            Assert.Equal(HttpStatusCode.BadRequest, stolen.StatusCode);
            using var duplicate = await Send(owner, HttpMethod.Post, "/api/bikes", BikeBody([first.Id, first.Id]));
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
            using var excessive = await Send(owner, HttpMethod.Post, "/api/bikes", BikeBody(Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()).ToArray()));
            Assert.Equal(HttpStatusCode.BadRequest, excessive.StatusCode);
            using var created = await Send(owner, HttpMethod.Post, "/api/bikes", BikeBody([first.Id, second.Id]));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var bike = (await created.Content.ReadFromJsonAsync<BikeDto>())!;
            Assert.Equal(new[] { first.Id, second.Id }, bike.Photos.Select(p => p.Id));
            Assert.Equal(first.ImageUrl, bike.ImageUrl);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(second.ImageUrl)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(third.ImageUrl)).StatusCode);
            using var postResponse = await Send(owner, HttpMethod.Post, "/api/posts", new { bikeId = bike.Id, title = "Ride with photos",
                content = "My first ride with a photo gallery. #ride", category = "ride", imageIds = new[] { third.Id, second.Id } });
            Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
            var post = (await postResponse.Content.ReadFromJsonAsync<PostDto>())!;
            Assert.Equal(new[] { third.Id, second.Id }, post.Photos.Select(p => p.Id));
            Assert.Equal(third.ImageUrl, post.ImageUrl);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(third.ImageUrl)).StatusCode);
            using var updated = await Send(owner, HttpMethod.Put, "/api/bikes/" + bike.Id, BikeBody([second.Id]));
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal(second.ImageUrl, (await updated.Content.ReadFromJsonAsync<BikeDto>())!.ImageUrl);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(first.ImageUrl)).StatusCode);
            // A fresh context/API read must retain both the gallery and its chosen cover.
            var fresh = (await anonymous.GetFromJsonAsync<PostDto>("/api/posts/" + post.Id))!;
            Assert.Equal(new[] { third.Id, second.Id }, fresh.Photos.Select(p => p.Id));
            Assert.Equal(third.ImageUrl, fresh.ImageUrl);
            var history = (await anonymous.GetFromJsonAsync<Page<PostDto>>($"/api/bikes/{bike.Id}/history"))!;
            Assert.Equal(post.Id, Assert.Single(history.Items).Id);
            using var deleted = await Send(owner, HttpMethod.Delete, "/api/posts/" + post.Id, new { });
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(third.ImageUrl)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(second.ImageUrl)).StatusCode);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
