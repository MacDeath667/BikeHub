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
public sealed class RideTests
{
    private static AuthFactory App(PersistenceTestDatabase database)
    {
        var app = new AuthFactory { Database = database };
        app.Settings["Authentication:Store"] = "Postgres"; app.Settings["Community:Store"] = "Postgres";
        app.Settings["Moderation:ModeratorEmails:0"] = "ride-owner@example.test";
        return app;
    }
    private static SaveRideRequest Request(DateTimeOffset start, int? capacity = 2) => new("Поездка по набережной", "Встречаемся и едем вместе", "Казань", "У входа в парк", start,
        "Europe/Moscow", 35, 20, RideDifficulty.Easy, capacity, MeetingPoint: new GeoPoint(55.8, 49.1),
        Route: new MapRoute("Маршрут покатушки", [[new(55.8, 49.1), new(55.9, 49.2)]], []));
    private static async Task<HttpResponseMessage> Change(HttpClient client, string path, object? body = null, HttpMethod? method = null)
    {
        var session = await client.GetFromJsonAsync<AuthBootstrap>("/api/auth/session");
        using var request = new HttpRequestMessage(method ?? HttpMethod.Put, path) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("X-CSRF-TOKEN", session!.CsrfToken);
        return await client.SendAsync(request);
    }
    private static async Task<T> Create<T>(HttpClient client, string path, object body)
    {
        using var response = await AuthTestClient.Post(client, path, body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<T> Success<T>(HttpResponseMessage response)
    {
        using (response) { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>())!; }
    }

    [Fact]
    public async Task Concurrent_attendance_cannot_overbook_and_is_idempotent_with_private_safe_rosters()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var owner = app.Browser(); using var first = app.Browser(); using var second = app.Browser(); using var guest = app.Browser();
        var (_, o) = await AuthTestClient.Login(owner, "ride-owner@example.test"); o.Dispose();
        var (_, a) = await AuthTestClient.Login(first, "ride-first@example.test"); a.Dispose();
        var (_, b) = await AuthTestClient.Login(second, "ride-second@example.test"); b.Dispose();
        var ride = await Create<RideDto>(owner, "/api/rides", Request(app.Clock.GetUtcNow().AddDays(1)));
        Assert.Equal(1, ride.Going); Assert.Equal(RideAttendance.Going, ride.Attendance); Assert.NotNull(ride.Route);
        var results = await Task.WhenAll(Change(first, $"/api/rides/{ride.Id}/attendance", new { attendance = 1 }), Change(second, $"/api/rides/{ride.Id}/attendance", new { attendance = 1 }));
        try
        {
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
            var winner = results[0].IsSuccessStatusCode ? first : second; var loser = results[0].IsSuccessStatusCode ? second : first;
            var repeated = await Success<RideDto>(await Change(winner, $"/api/rides/{ride.Id}/attendance", new { attendance = 1 })); Assert.Equal(2, repeated.Going);
            var maybe = await Success<RideDto>(await Change(loser, $"/api/rides/{ride.Id}/attendance", new { attendance = 2 })); Assert.Equal(2, maybe.Going); Assert.Equal(1, maybe.Maybe);
            using var rosterResponse = await guest.GetAsync($"/api/rides/{ride.Id}/participants?pageSize=2");
            Assert.True(rosterResponse.Headers.CacheControl!.NoStore); Assert.DoesNotContain("@example.test", await rosterResponse.Content.ReadAsStringAsync());
            var roster = (await rosterResponse.Content.ReadFromJsonAsync<Page<RideParticipantDto>>())!; Assert.Equal(3, roster.Total); Assert.Equal(2, roster.Items.Count); Assert.True(roster.HasMore);
            using var noCsrf = await winner.PutAsJsonAsync($"/api/rides/{ride.Id}/attendance", new { attendance = 2 }); Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
            using var ownLeave = await Change(owner, $"/api/rides/{ride.Id}/attendance", method: HttpMethod.Delete); Assert.Equal(HttpStatusCode.Conflict, ownLeave.StatusCode);
            using var shrink = await Change(owner, $"/api/rides/{ride.Id}", Request(ride.StartsAt, 1) with { Revision = ride.Revision }); Assert.Equal(HttpStatusCode.Conflict, shrink.StatusCode);
            var left = await Success<RideDto>(await Change(winner, $"/api/rides/{ride.Id}/attendance", method: HttpMethod.Delete)); Assert.Equal(1, left.Going);
            var joined = await Success<RideDto>(await Change(loser, $"/api/rides/{ride.Id}/attendance", new { attendance = 1 })); Assert.Equal(2, joined.Going); Assert.Equal(0, joined.Maybe);
        }
        finally { foreach (var result in results) { result.Dispose(); } }
        var list = (await guest.GetFromJsonAsync<Page<RideDto>>("/api/rides?city=" + Uri.EscapeDataString("каз")))!;
        Assert.True(Assert.Single(list.Items).HasRoute); Assert.Null(list.Items[0].Route);
        Assert.Empty((await guest.GetFromJsonAsync<Page<RideDto>>("/api/rides?city=%25"))!.Items);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/rides?mine=true")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync("/api/rides?pageSize=51")).StatusCode);
    }

    [Fact]
    public async Task Changes_use_revisions_notify_every_update_and_cancel_once_without_reopening_attendance()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var owner = app.Browser(); using var member = app.Browser(); using var guest = app.Browser();
        var (_, o) = await AuthTestClient.Login(owner, "ride-owner@example.test"); o.Dispose();
        var (_, m) = await AuthTestClient.Login(member, "ride-member@example.test"); m.Dispose();
        var request = Request(app.Clock.GetUtcNow().AddDays(2)); var ride = await Create<RideDto>(owner, "/api/rides", request);
        await Success<RideDto>(await Change(member, $"/api/rides/{ride.Id}/attendance", new { attendance = 1 }));
        using var forged = await Change(member, $"/api/rides/{ride.Id}", request with { Revision = ride.Revision }); Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
        var unchanged = await Success<RideDto>(await Change(owner, $"/api/rides/{ride.Id}", request with { Revision = ride.Revision })); Assert.Equal(ride.Revision, unchanged.Revision);
        Assert.Equal(0, await member.GetFromJsonAsync<int>("/api/notifications/unread-count"));
        var updated = await Success<RideDto>(await Change(owner, $"/api/rides/{ride.Id}", request with { Revision = ride.Revision, MeetingName = "У другого входа" }));
        using var stale = await Change(owner, $"/api/rides/{ride.Id}", request with { Revision = ride.Revision }); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var updatedAgain = await Success<RideDto>(await Change(owner, $"/api/rides/{ride.Id}", request with { Revision = updated.Revision, StartsAt = request.StartsAt.AddHours(1) }));
        var notices = (await member.GetFromJsonAsync<Page<NotificationDto>>("/api/notifications"))!;
        Assert.Equal(2, notices.Total); Assert.All(notices.Items, n => { Assert.Equal(NotificationKind.RideChanged, n.Kind); Assert.Equal(ride.Id, n.ContainerId); });
        var cancelled = await Create<RideDto>(owner, $"/api/rides/{ride.Id}/cancel", new { revision = updatedAgain.Revision }); Assert.Equal(RideStatus.Cancelled, cancelled.Status);
        var again = await Create<RideDto>(owner, $"/api/rides/{ride.Id}/cancel", new { revision = updatedAgain.Revision }); Assert.Equal(cancelled.Revision, again.Revision);
        Assert.Equal(3, await member.GetFromJsonAsync<int>("/api/notifications/unread-count"));
        using var closed = await Change(member, $"/api/rides/{ride.Id}/attendance", new { attendance = 1 }); Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
        Assert.Empty((await guest.GetFromJsonAsync<Page<RideDto>>("/api/rides"))!.Items);
        Assert.Single((await member.GetFromJsonAsync<Page<RideDto>>("/api/rides?mine=true"))!.Items);
        using (var db = database.CreateDbContext()) { await db.Rides.Where(r => r.Id == ride.Id).ExecuteUpdateAsync(u => u.SetProperty(r => r.IsHidden, true)); }
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/rides/{ride.Id}")).StatusCode);
        Assert.Empty((await member.GetFromJsonAsync<Page<NotificationDto>>("/api/notifications"))!.Items);
    }

    [Fact]
    public async Task Group_rides_validate_membership_dates_coordinates_and_follow_group_and_author_visibility()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var owner = app.Browser(); using var outsider = app.Browser(); using var guest = app.Browser();
        var (o, login) = await AuthTestClient.Login(owner, "ride-owner@example.test"); login.Dispose();
        var (_, other) = await AuthTestClient.Login(outsider, "ride-outsider@example.test"); other.Dispose();
        var group = await Create<CommunityDto>(owner, "/api/communities", new { name = "Ride group", description = "Fixture", category = "gravel" });
        var request = Request(app.Clock.GetUtcNow().AddDays(1)) with { GroupId = group.Id };
        using var forbidden = await AuthTestClient.Post(outsider, "/api/rides", request); Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        foreach (var invalid in new[] { request with { StartsAt = app.Clock.GetUtcNow().AddMinutes(-1) }, request with { TimeZone = "Missing/TimeZone" }, request with { MeetingPoint = new GeoPoint(91, 10) }, request with { Difficulty = (RideDifficulty)99 } })
        {
            using var response = await AuthTestClient.Post(owner, "/api/rides", invalid); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        var ride = await Create<RideDto>(owner, "/api/rides", request); Assert.Equal(group.Id, ride.GroupId);
        var windowsZone = await Create<RideDto>(owner, "/api/rides", request with { TimeZone = "Russian Standard Time", Title = "Совместимый часовой пояс" });
        Assert.Equal("Europe/Moscow", windowsZone.TimeZone);
        Assert.Equal(2, (await guest.GetFromJsonAsync<Page<RideDto>>($"/api/rides?groupId={group.Id}"))!.Items.Count);
        using (var db = database.CreateDbContext()) { await db.Communities.Where(g => g.Id == group.Id).ExecuteUpdateAsync(u => u.SetProperty(g => g.IsHidden, true)); }
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/rides/{ride.Id}/participants")).StatusCode);
        using (var db = database.CreateDbContext())
        {
            await db.Communities.Where(g => g.Id == group.Id).ExecuteUpdateAsync(u => u.SetProperty(g => g.IsHidden, false));
            await db.Accounts.Where(a => a.Id == o.User!.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.IsBlocked, true));
        }
        Assert.Empty((await guest.GetFromJsonAsync<Page<RideDto>>("/api/rides"))!.Items);
    }
}
