using System.Net;
using System.Net.Http.Json;
using BikeHub.Api.Authentication;
using BikeHub.Api.Services;
using BikeHub.Application.Community;
using BikeHub.Dal;
using ImageMagick;

namespace BikeHub.Api.Tests;

[Collection("Postgres persistence")]
public sealed class CommunityMembersTests
{
    private static async Task<HttpResponseMessage> Send(HttpClient browser, HttpMethod method, string path, object body)
    {
        var auth = await browser.GetFromJsonAsync<AuthBootstrap>("/api/auth/session");
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", auth!.CsrfToken);
        return await browser.SendAsync(request);
    }
    private static async Task<MediaDto> Upload(HttpClient browser)
    {
        using var image = new MagickImage(MagickColors.Green, 160, 80);
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(image.ToByteArray(MagickFormat.Png)), "file", "avatar.png");
        var auth = await browser.GetFromJsonAsync<AuthBootstrap>("/api/auth/session");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/media") { Content = form };
        request.Headers.Add("X-CSRF-TOKEN", auth!.CsrfToken);
        using var response = await browser.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MediaDto>())!;
    }
    [Fact]
    public async Task Community_avatar_enforces_ownership_and_member_list_is_paginated_public_and_filters_blocked_accounts()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        var root = Path.Combine(Path.GetTempPath(), "bikehub-community-avatar-" + Guid.NewGuid());
        try
        {
            await using var app = new AuthFactory { Database = database };
            app.Settings["Authentication:Store"] = "Postgres"; app.Settings["Community:Store"] = "Postgres";
            app.Settings["Media:RootPath"] = root;
            app.Settings["Moderation:ModeratorEmails:0"] = "moderator@example.test";
            using var owner = app.Browser(); using var member = app.Browser(); using var moderator = app.Browser(); using var guest = app.Browser();
            var (ownerAuth, ownerLogin) = await AuthTestClient.Login(owner, "owner-avatar@example.test", "Alpha Owner"); ownerLogin.Dispose();
            var (memberAuth, memberLogin) = await AuthTestClient.Login(member, "member-avatar@example.test", "Beta Member"); memberLogin.Dispose();
            (await AuthTestClient.Login(moderator, "moderator@example.test")).Response.Dispose();
            var avatar = await Upload(owner); var replacement = await Upload(owner); var foreign = await Upload(member);
            object Body(Guid? imageId) => new { name = "Avatar club", description = "A community with its own avatar", category = "gravel", imageId };
            using var created = await Send(owner, HttpMethod.Post, "/api/communities", Body(avatar.Id));
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var group = (await created.Content.ReadFromJsonAsync<CommunityDto>())!;
            Assert.Equal(avatar.Id, group.ImageId); Assert.Equal(avatar.ImageUrl, group.ImageUrl);
            Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync(avatar.ImageUrl)).StatusCode);
            using var joined = await Send(member, HttpMethod.Put, $"/api/communities/{group.Id}/membership", new { });
            Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
            using var firstResponse = await guest.GetAsync($"/api/communities/{group.Id}/members?pageSize=1");
            Assert.True(firstResponse.Headers.CacheControl!.NoStore);
            var first = (await firstResponse.Content.ReadFromJsonAsync<Page<UserDto>>())!;
            var second = (await guest.GetFromJsonAsync<Page<UserDto>>($"/api/communities/{group.Id}/members?pageSize=1&page=2"))!;
            Assert.Equal(2, first.Total); Assert.True(first.HasMore); Assert.False(second.HasMore);
            Assert.Equal(ownerAuth.User!.Id, Assert.Single(first.Items).Id);
            Assert.Equal(memberAuth.User!.Id, Assert.Single(second.Items).Id);
            Assert.DoesNotContain("@example.test", await guest.GetStringAsync($"/api/communities/{group.Id}/members"));
            using var stolen = await Send(owner, HttpMethod.Put, $"/api/communities/{group.Id}", Body(foreign.Id));
            Assert.Equal(HttpStatusCode.BadRequest, stolen.StatusCode);
            using var forbidden = await Send(member, HttpMethod.Put, $"/api/communities/{group.Id}", Body(foreign.Id));
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            using var updated = await Send(owner, HttpMethod.Put, $"/api/communities/{group.Id}", Body(replacement.Id));
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(avatar.ImageUrl)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync(replacement.ImageUrl)).StatusCode);
            using var retainedByModerator = await Send(moderator, HttpMethod.Put, $"/api/communities/{group.Id}", Body(replacement.Id));
            Assert.Equal(HttpStatusCode.OK, retainedByModerator.StatusCode);
            using var removed = await Send(owner, HttpMethod.Put, $"/api/communities/{group.Id}", Body(null));
            Assert.Equal(string.Empty, (await removed.Content.ReadFromJsonAsync<CommunityDto>())!.ImageUrl);
            Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(replacement.ImageUrl)).StatusCode);
            using var hidden = await Send(moderator, HttpMethod.Put, $"/api/moderation/6/{group.Id}/visibility", new { hidden = true });
            Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/communities/{group.Id}/members")).StatusCode);
            using var restored = await Send(moderator, HttpMethod.Put, $"/api/moderation/6/{group.Id}/visibility", new { hidden = false });
            using var banned = await Send(moderator, HttpMethod.Put, $"/api/moderation/users/{memberAuth.User.Id}/block", new { blocked = true });
            var visible = (await guest.GetFromJsonAsync<Page<UserDto>>($"/api/communities/{group.Id}/members"))!;
            Assert.Equal(1, visible.Total); Assert.Equal(ownerAuth.User.Id, Assert.Single(visible.Items).Id);
            Assert.Equal(1, (await guest.GetFromJsonAsync<CommunityDto>($"/api/communities/{group.Id}"))!.Members);
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
