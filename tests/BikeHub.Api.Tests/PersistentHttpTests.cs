using System.Net;
using System.Net.Http.Json;
using BikeHub.Api.Authentication;
using BikeHub.Api.Services;
using BikeHub.Application.Community;
using BikeHub.Dal;
using ImageMagick;

namespace BikeHub.Api.Tests;

[Collection("Postgres persistence")]
public sealed class PersistentHttpTests
{
    [Fact]
    public async Task Registration_upload_edit_and_ban_use_real_accounts_and_enforce_csrf_and_media_privacy()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        var root = Path.Combine(Path.GetTempPath(), "bikehub-media-test-" + Guid.NewGuid());
        try
        {
            using var factory = new AuthFactory { Database = database };
            factory.Settings["Community:Store"] = "Postgres";
            factory.Settings["Authentication:Store"] = "Postgres";
            factory.Settings["Media:RootPath"] = root;
            factory.Settings["Moderation:ModeratorEmails:0"] = "moderator@example.test";
            using var owner = factory.Browser();
            using var other = factory.Browser();
            using var anonymous = factory.Browser();
            var (session, response) = await AuthTestClient.Login(owner, "owner@example.test");
            response.Dispose();
            var (moderator, moderatorResponse) = await AuthTestClient.Login(other, "moderator@example.test");
            moderatorResponse.Dispose();
            Assert.True(moderator.User!.IsModerator);
            var bootstrap = await owner.GetFromJsonAsync<AuthBootstrap>("/api/auth/session");
            async Task<HttpResponseMessage> Upload(byte[] bytes, string filename, bool csrf = true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/media");
                var form = new MultipartFormDataContent();
                form.Add(new ByteArrayContent(bytes), "file", filename);
                request.Content = form;
                if (csrf)
                {
                    request.Headers.Add("X-CSRF-TOKEN", bootstrap!.CsrfToken);
                }
                return await owner.SendAsync(request);
            }
            using var image = new MagickImage(MagickColors.Green, 2000, 1000);
            var bytes = image.ToByteArray(MagickFormat.Png);
            using var missingCsrf = await Upload(bytes, "photo.png", false);
            Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
            using var invalid = await Upload("<svg onload='alert(1)'/>"u8.ToArray(), "photo.png");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var upload = await Upload(bytes, "../../outside.png");
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
            var media = (await upload.Content.ReadFromJsonAsync<MediaDto>())!;
            Assert.Equal(1600, media.Width);
            Assert.Equal(800, media.Height);
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(media.ImageUrl)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(media.ImageUrl)).StatusCode);
            using var bikeResponse = await AuthTestClient.Post(owner, "/api/bikes", new
            { name = "Real bike", brand = "Brand", category = "gravel", year = 2024, mileage = 5, description = "Test", imageId = media.Id });
            Assert.Equal(HttpStatusCode.Created, bikeResponse.StatusCode);
            var bike = (await bikeResponse.Content.ReadFromJsonAsync<BikeDto>())!;
            using var publicImage = await anonymous.GetAsync(media.ImageUrl);
            Assert.Equal(HttpStatusCode.OK, publicImage.StatusCode);
            Assert.Equal("image/webp", publicImage.Content.Headers.ContentType!.MediaType);
            async Task<HttpResponseMessage> Put(HttpClient client, string path, object body)
            {
                var auth = await client.GetFromJsonAsync<AuthBootstrap>("/api/auth/session");
                using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
                request.Headers.Add("X-CSRF-TOKEN", auth!.CsrfToken);
                return await client.SendAsync(request);
            }
            using var forged = await Put(other, "/api/bikes/" + bike.Id, new
            { name = "Stolen bike", brand = "Brand", category = "gravel", year = 2024, mileage = 5, description = "Test" });
            Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
            using var profile = await Put(owner, "/api/users/me", new { name = "New name", handle = session.User!.Handle, city = "City", bio = "Bio", avatarId = media.Id });
            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
            Assert.Equal("New name", (await owner.GetFromJsonAsync<AuthBootstrap>("/api/auth/session"))!.User!.Name);
            using var banned = await Put(other, $"/api/moderation/users/{session.User!.Id}/block", new { blocked = true });
            Assert.Equal(HttpStatusCode.NoContent, banned.StatusCode);
            Assert.Null((await owner.GetFromJsonAsync<AuthBootstrap>("/api/auth/session"))!.User);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(media.ImageUrl)).StatusCode);
            using var login = await AuthTestClient.Post(owner, "/api/auth/login/email/challenges", new { email = "owner@example.test" });
            Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
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
