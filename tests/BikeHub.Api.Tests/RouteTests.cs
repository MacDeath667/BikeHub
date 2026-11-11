using System.Net;
using System.Net.Http.Json;
using System.Text;
using BikeHub.Api.Authentication;
using BikeHub.Api.Services;
using BikeHub.Application.Community;
using BikeHub.Dal;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Tests;

[Collection("Postgres persistence")]
public sealed class RouteTests
{
    private const string Gpx = """
        <gpx xmlns="http://www.topografix.com/GPX/1/1" version="1.1" creator="Private device">
          <metadata><author><name>Private user</name></author></metadata>
          <wpt lat="55.7" lon="37.6"><name>&lt;script&gt;alert(1)&lt;/script&gt;</name></wpt>
          <trk><trkseg><trkpt lat="55.7" lon="37.6"><time>2026-11-11T08:00:00Z</time></trkpt><trkpt lat="55.71" lon="37.61"/></trkseg>
          <trkseg><trkpt lat="55.8" lon="37.8"/><trkpt lat="55.81" lon="37.81"/></trkseg></trk>
        </gpx>
        """;
    private static MapRoute Parse(string xml) => RouteService.Import(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
    [Fact]
    public void Gpx_preserves_segments_order_and_waypoints_but_discards_private_metadata()
    {
        var route = Parse(Gpx);
        Assert.Equal(2, route.Segments.Length);
        Assert.Equal(new GeoPoint(55.71, 37.61), route.Segments[0][1]);
        Assert.Equal(new GeoPoint(55.8, 37.8), route.Segments[1][0]);
        Assert.Single(route.Markers);
        var saved = RouteService.Save(route)!;
        Assert.DoesNotContain("Private", saved);
        Assert.DoesNotContain("2026-11", saved);
        Assert.DoesNotContain("script", saved);
        var rte = Parse("<gpx><rte><rtept lat='0' lon='0'/><rtept lat='1' lon='1'/></rte></gpx>");
        Assert.Single(rte.Segments);
        Assert.Equal(2, rte.Segments[0].Length);
    }
    [Theory]
    [InlineData("<!DOCTYPE gpx [<!ENTITY xxe SYSTEM 'file:///etc/passwd'>]><gpx>&xxe;</gpx>")]
    [InlineData("<html><wpt lat='1' lon='2'/></html>")]
    [InlineData("<gpx><wpt lat='91' lon='2'/></gpx>")]
    [InlineData("<gpx><wpt lat='NaN' lon='2'/></gpx>")]
    [InlineData("<gpx><wpt lat='1' lon='181'/></gpx>")]
    [InlineData("<gpx/>")]
    [InlineData("<gpx><extensions><wpt lat='1' lon='2'/></extensions></gpx>")]
    public void Invalid_or_unsafe_gpx_is_rejected(string xml) => Assert.Equal(400, Assert.Throws<CommunityProblem>(() => Parse(xml)).Status);

    [Fact]
    public void Oversized_deep_or_invalid_routes_are_rejected()
    {
        Assert.Throws<CommunityProblem>(() => Parse("<gpx>" + string.Concat(Enumerable.Repeat("<wpt lat='1' lon='2'/>", 101)) + "</gpx>"));
        Assert.Throws<CommunityProblem>(() => Parse("<gpx>" + string.Concat(Enumerable.Repeat("<x>", 40)) + string.Concat(Enumerable.Repeat("</x>", 40)) + "</gpx>"));
        Assert.Throws<CommunityProblem>(() => RouteService.Save(new("Test", [new GeoPoint[RouteService.MaximumPoints + 1]], [])));
        Assert.Throws<CommunityProblem>(() => RouteService.Save(new("Test", [[new(1, 2)]], [])));
        Assert.Throws<CommunityProblem>(() => RouteService.Save(new("Test", [], [new(90, 181, "bad")])));
    }

    private static async Task<HttpResponseMessage> Send(HttpClient browser, HttpMethod method, string path, object body)
    {
        var session = await browser.GetFromJsonAsync<AuthBootstrap>("/api/auth/session");
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", session!.CsrfToken);
        return await browser.SendAsync(request);
    }
    [Fact]
    public async Task Imported_and_manual_routes_persist_in_posts_and_groups_follow_ownership_and_visibility_and_can_be_removed()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = new AuthFactory { Database = database };
        app.Settings["Authentication:Store"] = "Postgres"; app.Settings["Community:Store"] = "Postgres";
        app.Settings["Moderation:ModeratorEmails:0"] = "moderator@example.test";
        using var owner = app.Browser(); using var moderator = app.Browser(); using var guest = app.Browser();
        (await AuthTestClient.Login(owner, "route@example.test")).Response.Dispose();
        (await AuthTestClient.Login(moderator, "moderator@example.test")).Response.Dispose();
        using var multipart = new MultipartFormDataContent(); multipart.Add(new StringContent(Gpx), "file", "route.gpx");
        using var import = new HttpRequestMessage(HttpMethod.Post, "/api/routes/import") { Content = multipart };
        var auth = await owner.GetFromJsonAsync<AuthBootstrap>("/api/auth/session"); import.Headers.Add("X-CSRF-TOKEN", auth!.CsrfToken);
        using var imported = await owner.SendAsync(import); Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var route = (await imported.Content.ReadFromJsonAsync<MapRoute>())!;
        using var bikeResponse = await Send(owner, HttpMethod.Post, "/api/bikes", new { name = "Route bike", brand = "Brand", category = "gravel", year = 2026, mileage = 0, description = "Bike" });
        var bike = (await bikeResponse.Content.ReadFromJsonAsync<BikeDto>())!;
        using var missingCoordinates = await Send(owner, HttpMethod.Post, "/api/posts", new { bikeId = bike.Id, title = "Invalid route", content = "Missing latitude in coordinates", category = "ride",
            route = new { name = "Invalid", segments = new[] { new[] { new { lng = 2 }, new { lng = 3 } } }, markers = Array.Empty<object>() } });
        Assert.Equal(HttpStatusCode.BadRequest, missingCoordinates.StatusCode);
        object Body(MapRoute? value) => new { bikeId = bike.Id, title = "Ride with route", content = "A ride with GPS coordinates", category = "ride", route = value };
        using var postResponse = await Send(owner, HttpMethod.Post, "/api/posts", Body(route)); Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var post = (await postResponse.Content.ReadFromJsonAsync<PostDto>())!;
        // PostDto.Route is a computed response property; inspect the public JSON directly as a client does.
        var json = await guest.GetStringAsync($"/api/posts/{post.Id}");
        Assert.Contains("\"segments\"", json); Assert.DoesNotContain("routeJson", json);
        Assert.DoesNotContain("Private device", json);
        using var forged = await Send(moderator, HttpMethod.Put, $"/api/posts/{post.Id}", Body(null)); Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
        using var hidden = await Send(moderator, HttpMethod.Put, $"/api/moderation/2/{post.Id}/visibility", new { hidden = true });
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/posts/{post.Id}")).StatusCode);
        using var restored = await Send(moderator, HttpMethod.Put, $"/api/moderation/2/{post.Id}/visibility", new { hidden = false });
        var manual = new MapRoute("Manual", [[new(1, 2), new(3, 4)]], [new(1, 2, "Start")]);
        using var updated = await Send(owner, HttpMethod.Put, $"/api/posts/{post.Id}", Body(manual)); Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        await using (var db = await database.CreateDbContextAsync())
        {
            var row = await db.Posts.SingleAsync(p => p.Id == post.Id);
            Assert.Equal(manual.Name, RouteJson.Read(row.RouteJson)!.Name);
            Assert.False(db.Database.HasPendingModelChanges());
        }
        using var groupResponse = await Send(owner, HttpMethod.Post, "/api/communities", new { name = "Route club", description = "Routes", category = "gravel" });
        var group = (await groupResponse.Content.ReadFromJsonAsync<CommunityDto>())!;
        using var entry = await Send(owner, HttpMethod.Post, $"/api/communities/{group.Id}/entries", new { kind = 1, title = "Club route", content = "Shared route", route = manual });
        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        Assert.Contains("Manual", await entry.Content.ReadAsStringAsync());
        using var removed = await Send(owner, HttpMethod.Put, $"/api/posts/{post.Id}", Body(null)); Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Contains("\"route\":null", await guest.GetStringAsync($"/api/posts/{post.Id}"));
    }
}
