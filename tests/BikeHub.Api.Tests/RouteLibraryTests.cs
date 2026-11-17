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
public sealed class RouteLibraryTests
{
    private static AuthFactory App(PersistenceTestDatabase database)
    {
        var app = new AuthFactory { Database = database };
        app.Settings["Authentication:Store"] = "Postgres"; app.Settings["Community:Store"] = "Postgres"; return app;
    }
    private static SaveLibraryRouteRequest Request() => new("Маршрут у реки", "Спокойная дорога вдоль воды", "Казань", RouteSurface.Gravel,
        new MapRoute("Речной маршрут", [[new(0, 0), new(0, 0.1)]], [new(0, 0, "Начало")]));
    private static async Task<T> Create<T>(HttpClient client, string path, object body)
    {
        using var response = await AuthTestClient.Post(client, path, body); Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<HttpResponseMessage> Change(HttpClient client, string path, object? body = null, HttpMethod? method = null)
    {
        var session = await client.GetFromJsonAsync<AuthBootstrap>("/api/auth/session");
        using var request = new HttpRequestMessage(method ?? HttpMethod.Put, path) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("X-CSRF-TOKEN", session!.CsrfToken); return await client.SendAsync(request);
    }
    private static async Task<T> Success<T>(HttpResponseMessage response)
    {
        using (response) { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>())!; }
    }

    [Fact]
    public async Task Personal_collections_and_notes_are_isolated_and_repeat_saves_do_not_duplicate_routes()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var owner = app.Browser(); using var rider = app.Browser(); using var other = app.Browser(); using var guest = app.Browser();
        var (_, o) = await AuthTestClient.Login(owner, "library-owner@example.test"); o.Dispose();
        var (_, r) = await AuthTestClient.Login(rider, "library-rider@example.test"); r.Dispose();
        var (_, t) = await AuthTestClient.Login(other, "library-other@example.test"); t.Dispose();
        var route = await Create<LibraryRouteDto>(owner, "/api/route-library", Request()); Assert.InRange(route.DistanceKm, 11.11m, 11.13m);
        var collection = await Create<RouteCollectionDto>(rider, "/api/route-collections", new { name = "На выходные" });
        using var duplicate = await AuthTestClient.Post(rider, "/api/route-collections", new { name = " НА ВЫХОДНЫЕ " }); Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var saved = await Success<LibraryRouteDto>(await Change(rider, $"/api/route-library/{route.Id}/bookmark", new { collectionId = collection.Id, note = "PRIVATE_RIDER_NOTE" }));
        Assert.True(saved.IsSaved); Assert.Equal(collection.Id, saved.CollectionId); Assert.Equal("PRIVATE_RIDER_NOTE", saved.MyNote);
        await Success<LibraryRouteDto>(await Change(rider, $"/api/route-library/{route.Id}/bookmark", new { collectionId = collection.Id, note = "PRIVATE_RIDER_NOTE" }));
        Assert.Equal(1, Assert.Single((await rider.GetFromJsonAsync<RouteCollectionDto[]>("/api/route-collections"))!).RoutesCount);
        Assert.Empty((await other.GetFromJsonAsync<RouteCollectionDto[]>("/api/route-collections"))!);
        foreach (var client in new[] { guest, owner, other })
        {
            using var response = await client.GetAsync($"/api/route-library/{route.Id}"); Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.DoesNotContain("PRIVATE_RIDER_NOTE", await response.Content.ReadAsStringAsync());
            var detail = (await response.Content.ReadFromJsonAsync<LibraryRouteDto>())!; Assert.False(detail.IsSaved); Assert.Null(detail.CollectionId); Assert.Null(detail.MyNote);
        }
        using var steal = await Change(other, $"/api/route-library/{route.Id}/bookmark", new { collectionId = collection.Id, note = "Forged" }); Assert.Equal(HttpStatusCode.NotFound, steal.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/route-library?collectionId={collection.Id}")).StatusCode);
        using var remove = await Change(rider, $"/api/route-collections/{collection.Id}", method: HttpMethod.Delete); Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        var retained = (await rider.GetFromJsonAsync<LibraryRouteDto>($"/api/route-library/{route.Id}"))!; Assert.True(retained.IsSaved); Assert.Null(retained.CollectionId); Assert.Equal(saved.MyNote, retained.MyNote);
        var list = (await rider.GetFromJsonAsync<Page<LibraryRouteDto>>("/api/route-library?saved=true&surface=2&minimum=11&maximum=12&city=" + Uri.EscapeDataString("каз")))!;
        Assert.Single(list.Items); Assert.Null(list.Items[0].Route); Assert.Null(list.Items[0].MyNote);
        Assert.Empty((await guest.GetFromJsonAsync<Page<LibraryRouteDto>>("/api/route-library?maximum=10"))!.Items);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/route-library?saved=true")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync("/api/route-library?minimum=100&maximum=10")).StatusCode);
    }

    [Fact]
    public async Task Reviews_update_in_place_notify_once_and_follow_source_and_author_visibility()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var owner = app.Browser(); using var rider = app.Browser(); using var guest = app.Browser();
        var (_, o) = await AuthTestClient.Login(owner, "review-route-owner@example.test"); o.Dispose();
        var (r, login) = await AuthTestClient.Login(rider, "review-route-rider@example.test"); login.Dispose();
        var request = Request(); var route = await Create<LibraryRouteDto>(owner, "/api/route-library", request);
        var date = DateOnly.FromDateTime(app.Clock.GetUtcNow().UtcDateTime);
        var review = await Success<RouteReviewDto>(await Change(rider, $"/api/route-library/{route.Id}/reviews/me", new { rating = 5, text = "Проехал, хорошая дорога", riddenOn = date }));
        var updated = await Success<RouteReviewDto>(await Change(rider, $"/api/route-library/{route.Id}/reviews/me", new { rating = 4, text = "После дождя грязно", riddenOn = date }));
        Assert.Equal(review.Id, updated.Id);
        var detail = (await guest.GetFromJsonAsync<LibraryRouteDto>($"/api/route-library/{route.Id}"))!; Assert.Equal(1, detail.ReviewsCount); Assert.Equal(4d, detail.Rating);
        var notification = Assert.Single((await owner.GetFromJsonAsync<Page<NotificationDto>>("/api/notifications"))!.Items);
        Assert.Equal(NotificationKind.RouteReview, notification.Kind); Assert.Equal(review.Id, notification.TargetId); Assert.Equal(route.Id, notification.ContainerId);
        using var future = await Change(rider, $"/api/route-library/{route.Id}/reviews/me", new { rating = 4, text = "Будущая поездка", riddenOn = date.AddDays(1) }); Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        using var stolen = await Change(rider, $"/api/route-library/{route.Id}", request with { Revision = route.Revision }); Assert.Equal(HttpStatusCode.Forbidden, stolen.StatusCode);
        await Success<LibraryRouteDto>(await Change(owner, $"/api/route-library/{route.Id}", request with { Revision = route.Revision, Title = "Новый заголовок" }));
        using var stale = await Change(owner, $"/api/route-library/{route.Id}", request with { Revision = route.Revision }); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var db = database.CreateDbContext()) { await db.Accounts.Where(a => a.Id == r.User!.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.IsBlocked, true)); }
        Assert.Empty((await guest.GetFromJsonAsync<Page<RouteReviewDto>>($"/api/route-library/{route.Id}/reviews"))!.Items);
        Assert.Equal(0, (await guest.GetFromJsonAsync<LibraryRouteDto>($"/api/route-library/{route.Id}"))!.ReviewsCount);
        Assert.Empty((await owner.GetFromJsonAsync<Page<NotificationDto>>("/api/notifications"))!.Items);
        using (var db = database.CreateDbContext()) { await db.LibraryRoutes.Where(x => x.Id == route.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsHidden, true)); }
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/route-library/{route.Id}/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/route-library/{route.Id}/reviews")).StatusCode);
    }
}
