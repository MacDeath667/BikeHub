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
public sealed class MaintenanceTests
{
    private static AuthFactory App(PersistenceTestDatabase database)
    {
        var app = new AuthFactory { Database = database };
        app.Settings["Authentication:Store"] = "Postgres"; app.Settings["Community:Store"] = "Postgres"; return app;
    }
    private static async Task<T> Create<T>(HttpClient client, string path, object body)
    {
        using var response = await AuthTestClient.Post(client, path, body); Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<HttpResponseMessage> Change(HttpClient client, string path, object? body = null, HttpMethod? method = null)
    {
        var session = await client.GetFromJsonAsync<AuthBootstrap>("/api/auth/session"); using var request = new HttpRequestMessage(method ?? HttpMethod.Put, path) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("X-CSRF-TOKEN", session!.CsrfToken); return await client.SendAsync(request);
    }
    private static async Task<T> Success<T>(HttpResponseMessage response)
    {
        using (response) { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>())!; }
    }
    private static Task<BikeDto> Bike(HttpClient client, decimal mileage) => Create<BikeDto>(client, "/api/bikes", new { name = "Service bicycle", brand = "Fixture", category = "gravel", year = 2026, mileage, description = "Bicycle" });
    private static SaveBikeComponentRequest Component(DateOnly date) => new(BikeComponentKind.Chain, "Передняя цепь", "Fixture chain", "PRIVATE_COMPONENT_NOTE", 0, date, 100);

    [Fact]
    public async Task Maintenance_is_owner_private_and_history_advances_but_never_rewinds_the_odometer()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var owner = app.Browser(); using var other = app.Browser(); using var guest = app.Browser();
        var (_, o) = await AuthTestClient.Login(owner, "maintenance-owner@example.test"); o.Dispose(); var (_, t) = await AuthTestClient.Login(other, "maintenance-other@example.test"); t.Dispose();
        var bike = await Bike(owner, 90); var path = $"/api/bikes/{bike.Id}/maintenance"; var date = DateOnly.FromDateTime(app.Clock.GetUtcNow().UtcDateTime);
        var component = await Create<BikeComponentDto>(owner, path + "/components", Component(date));
        var work = await Create<MaintenanceRecordDto>(owner, path + "/records", new SaveMaintenanceRecordRequest(MaintenanceKind.Service, "Смазка цепи", "PRIVATE_WORK_NOTE", date, 150, component.Id, 1234.50m, true));
        Assert.Equal(1234.50m, work.CostRub); Assert.Equal(component.Id, work.ComponentId);
        Assert.Equal(150, (await owner.GetFromJsonAsync<MaintenanceOverviewDto>(path))!.Mileage);
        await Create<MaintenanceRecordDto>(owner, path + "/records", new SaveMaintenanceRecordRequest(MaintenanceKind.Inspection, "Исторический осмотр", "", date, 50));
        Assert.Equal(150, (await owner.GetFromJsonAsync<MaintenanceOverviewDto>(path))!.Mileage);
        foreach (var suffix in new[] { "", "/components", "/components/options", "/records" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(path + suffix)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync(path + suffix)).StatusCode);
        }
        foreach (var client in new[] { other, guest })
        {
            var publicBike = await client.GetStringAsync($"/api/bikes/{bike.Id}"); Assert.DoesNotContain("PRIVATE_", publicBike); Assert.DoesNotContain("costRub", publicBike);
        }
        using var response = await owner.GetAsync(path + "/records"); Assert.True(response.Headers.CacheControl!.NoStore);
        var page = (await response.Content.ReadFromJsonAsync<Page<MaintenanceRecordDto>>())!; Assert.Equal(2, page.Total); Assert.Contains(page.Items, r => r.Notes == "PRIVATE_WORK_NOTE");
        using var remove = await Change(owner, path + "/records/" + work.Id, method: HttpMethod.Delete); Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Equal(150, (await owner.GetFromJsonAsync<MaintenanceOverviewDto>(path))!.Mileage);
        using var forged = await Change(other, path + "/mileage", new { mileage = 999 }); Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
    }

    [Fact]
    public async Task Reminders_follow_service_cycles_are_deduplicated_and_archive_hides_them()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var owner = app.Browser();
        var (_, login) = await AuthTestClient.Login(owner, "cycle-owner@example.test"); login.Dispose();
        var bike = await Bike(owner, 90); var path = $"/api/bikes/{bike.Id}/maintenance"; var date = DateOnly.FromDateTime(app.Clock.GetUtcNow().UtcDateTime);
        var request = Component(date); var component = await Create<BikeComponentDto>(owner, path + "/components", request);
        Assert.Equal(0, await owner.GetFromJsonAsync<int>("/api/notifications/unread-count"));
        await Success<MaintenanceOverviewDto>(await Change(owner, path + "/mileage", new { mileage = 100 }));
        await Success<MaintenanceOverviewDto>(await Change(owner, path + "/mileage", new { mileage = 101 }));
        var notification = Assert.Single((await owner.GetFromJsonAsync<Page<NotificationDto>>("/api/notifications"))!.Items); Assert.Equal(NotificationKind.MaintenanceDue, notification.Kind); Assert.Equal(bike.Id, notification.ContainerId);
        Assert.Equal(1, (await owner.GetFromJsonAsync<MaintenanceOverviewDto>(path))!.DueComponents);
        await Create<MaintenanceRecordDto>(owner, path + "/records", new SaveMaintenanceRecordRequest(MaintenanceKind.Service, "Плановое обслуживание", "", date, 100, component.Id, ResetsInterval: true));
        Assert.Equal(0, await owner.GetFromJsonAsync<int>("/api/notifications/unread-count"));
        var refreshed = Assert.Single((await owner.GetFromJsonAsync<Page<BikeComponentDto>>(path + "/components"))!.Items); Assert.Equal(200, refreshed.DueMileage);
        // The existing public bike editor also refreshes maintenance reminders.
        await Success<BikeDto>(await Change(owner, $"/api/bikes/{bike.Id}", new { name = bike.Name, brand = bike.Brand, category = "gravel", year = 2026, mileage = 200, description = bike.Description }));
        var next = Assert.Single((await owner.GetFromJsonAsync<Page<NotificationDto>>("/api/notifications"))!.Items); Assert.NotEqual(notification.Id, next.Id);
        var archived = await Success<BikeComponentDto>(await Change(owner, path + "/components/" + component.Id, request with { Revision = refreshed.Revision, IsArchived = true })); Assert.True(archived.IsArchived);
        Assert.Equal(0, await owner.GetFromJsonAsync<int>("/api/notifications/unread-count"));
        Assert.Equal(0, (await owner.GetFromJsonAsync<MaintenanceOverviewDto>(path))!.ActiveComponents);
        Assert.Single((await owner.GetFromJsonAsync<Page<BikeComponentDto>>(path + "/components?archived=true"))!.Items);
        using (var db = database.CreateDbContext()) { Assert.Equal(2, await db.MaintenanceReminders.CountAsync()); }
    }

    [Fact]
    public async Task Components_and_records_reject_foreign_references_invalid_dates_and_stale_revisions()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var owner = app.Browser();
        var (_, login) = await AuthTestClient.Login(owner, "component-owner@example.test"); login.Dispose();
        var bike = await Bike(owner, 100); var other = await Bike(owner, 100); var path = $"/api/bikes/{bike.Id}/maintenance"; var date = DateOnly.FromDateTime(app.Clock.GetUtcNow().UtcDateTime);
        var request = Component(date); var component = await Create<BikeComponentDto>(owner, path + "/components", request);
        using var future = await AuthTestClient.Post(owner, path + "/components", request with { InstalledOn = date.AddDays(1) }); Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        using var impossible = await AuthTestClient.Post(owner, path + "/components", request with { InstalledMileage = 101 }); Assert.Equal(HttpStatusCode.BadRequest, impossible.StatusCode);
        using var foreign = await AuthTestClient.Post(owner, $"/api/bikes/{other.Id}/maintenance/records", new SaveMaintenanceRecordRequest(MaintenanceKind.Service, "Чужой компонент", "", date, 100, component.Id)); Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        var updated = await Success<BikeComponentDto>(await Change(owner, path + "/components/" + component.Id, request with { Revision = component.Revision, Name = "Новая подпись" }));
        using var stale = await Change(owner, path + "/components/" + component.Id, request with { Revision = component.Revision }); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var work = await Create<MaintenanceRecordDto>(owner, path + "/records", new SaveMaintenanceRecordRequest(MaintenanceKind.Repair, "Подтяжка", "", date, 50, component.Id));
        using var badInstall = await Change(owner, path + "/components/" + component.Id, request with { Revision = updated.Revision, InstalledMileage = 60 }); Assert.Equal(HttpStatusCode.BadRequest, badInstall.StatusCode);
        using var invalidReset = await AuthTestClient.Post(owner, path + "/records", new SaveMaintenanceRecordRequest(MaintenanceKind.Service, "Без компонента", "", date, 100, ResetsInterval: true)); Assert.Equal(HttpStatusCode.BadRequest, invalidReset.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync(path + "/records?pageSize=51")).StatusCode);
        using var noCsrf = await owner.PutAsJsonAsync(path + "/mileage", new { mileage = 1000 }); Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        Assert.Equal(work.Id, Assert.Single((await owner.GetFromJsonAsync<Page<MaintenanceRecordDto>>(path + "/records?componentId=" + component.Id))!.Items).Id);
    }
}
