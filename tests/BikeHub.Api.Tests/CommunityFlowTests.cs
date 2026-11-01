using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BikeHub.Api.Authentication;

namespace BikeHub.Api.Tests;

public sealed class CommunityFlowTests
{
    [Fact]
    public async Task Rider_can_sign_in_create_bike_publish_comment_and_use_idempotent_reactions()
    {
        await using var app = new AuthFactory();
        using var browser = app.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/users/me")).StatusCode);
        await AuthTestClient.Login(browser, "community-flow@example.test");
        var me = await Get(browser, "/api/users/me");
        var meId = me.GetProperty("id").GetGuid();
        var bike = await Change(browser, HttpMethod.Post, "/api/bikes", new
        {
            name = "Smoke Gravel", brand = "Canyon", category = "gravel", year = 2024,
            mileage = 120, description = "Тестовый велосипед"
        }, HttpStatusCode.Created);
        var bikeId = bike.GetProperty("id").GetGuid();
        Assert.Equal(meId, bike.GetProperty("ownerId").GetGuid());
        var garage = await Get(browser, $"/api/users/{meId}/bikes?pageSize=50");
        Assert.Contains(garage.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == bikeId);
        var post = await Change(browser, HttpMethod.Post, "/api/posts", new
        {
            bikeId, title = "Проверка нового бортжурнала", content = "Первая запись для сквозной проверки API.", category = "ride"
        }, HttpStatusCode.Created);
        var postId = post.GetProperty("id").GetGuid();
        var history = await Get(browser, $"/api/bikes/{bikeId}/history");
        Assert.Equal(postId, history.GetProperty("items")[0].GetProperty("id").GetGuid());
        var feed = await Get(browser, "/api/feed");
        Assert.Equal(postId, feed.GetProperty("items")[0].GetProperty("id").GetGuid());
        var liked = await Change(browser, HttpMethod.Put, $"/api/posts/{postId}/like");
        var repeated = await Change(browser, HttpMethod.Put, $"/api/posts/{postId}/like");
        Assert.True(liked.GetProperty("isLiked").GetBoolean());
        Assert.Equal(liked.GetProperty("likes").GetInt32(), repeated.GetProperty("likes").GetInt32());
        await Change(browser, HttpMethod.Put, $"/api/posts/{postId}/bookmark");
        var saved = await Get(browser, "/api/feed?tab=saved");
        Assert.Contains(saved.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == postId);
        await Change(browser, HttpMethod.Post, $"/api/posts/{postId}/comments", new { text = " " }, HttpStatusCode.BadRequest);
        var comment = await Change(browser, HttpMethod.Post, $"/api/posts/{postId}/comments",
            new { text = "Первый комментарий из интеграционного теста." }, HttpStatusCode.Created);
        var comments = await Get(browser, $"/api/posts/{postId}/comments");
        Assert.Equal(comment.GetProperty("id").GetGuid(), comments.GetProperty("items")[0].GetProperty("id").GetGuid());
        var search = await Get(browser, "/api/feed?q=" + Uri.EscapeDataString("Проверка нового бортжурнала"));
        Assert.Contains(search.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == postId);
        var filtered = await Get(browser, "/api/feed?category=gravel");
        Assert.All(filtered.GetProperty("items").EnumerateArray(), item => Assert.Equal("gravel", item.GetProperty("bike").GetProperty("category").GetString()));
        var popular = await Get(browser, "/api/feed?tab=popular");
        var likes = popular.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("likes").GetInt32()).ToArray();
        Assert.Equal(likes.OrderDescending(), likes);
        var community = (await Get(browser, "/api/communities"))[0];
        var communityId = community.GetProperty("id").GetGuid();
        var joined = await Change(browser, HttpMethod.Put, $"/api/communities/{communityId}/membership");
        var joinedAgain = await Change(browser, HttpMethod.Put, $"/api/communities/{communityId}/membership");
        Assert.Equal(joined.GetProperty("members").GetInt32(), joinedAgain.GetProperty("members").GetInt32());
        await Change(browser, HttpMethod.Post, "/api/auth/logout");
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/users/me")).StatusCode);
    }

    private static async Task<JsonElement> Get(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> Change(HttpClient client, HttpMethod method, string path, object? body = null,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        var session = await client.GetFromJsonAsync<AuthBootstrap>("/api/auth/session");
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("X-CSRF-TOKEN", session!.CsrfToken);
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
