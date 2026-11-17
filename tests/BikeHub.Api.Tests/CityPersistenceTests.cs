using System.Net;
using System.Net.Http.Json;
using BikeHub.Application.Community;
using BikeHub.Api.Services;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BikeHub.Api.Tests;

[Collection("Postgres persistence")]
public sealed class CityPersistenceTests
{
    [Fact]
    public async Task Migration_normalizes_all_city_columns_but_preserves_unknown_and_ambiguous_legacy_places_on_replay()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        using var db = database.CreateDbContext();
        await db.GetService<IMigrator>().MigrateAsync("20261117102805_PrivateMessages");
        var owner = new AuthAccountEntity { Id = Guid.CreateVersion7(), Email = "city-migration@example.test", DisplayName = "Rider", City = "ПиТеР" };
        db.Accounts.AddRange(owner,
            new AuthAccountEntity { Id = Guid.CreateVersion7(), Email = "ambiguous-city@example.test", City = "Киров" },
            new AuthAccountEntity { Id = Guid.CreateVersion7(), Email = "unknown-city@example.test", City = "Старый неизвестный город" });
        db.Rides.Add(new GroupRide { Id = Guid.CreateVersion7(), OwnerId = owner.Id, Title = "Ride", City = " САНКТ ПЕТЕРБУРГ ", Revision = 1, Status = RideStatus.Scheduled, Difficulty = RideDifficulty.Easy });
        db.LibraryRoutes.Add(new LibraryRoute { Id = Guid.CreateVersion7(), OwnerId = owner.Id, Title = "Route", City = "г. СПб", Surface = RouteSurface.Mixed, Revision = 1, RouteJson = """{"name":"Route","segments":[],"markers":[]}""" });
        await db.SaveChangesAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261117120000_CanonicalCities'");
        await new DatabaseMigrator(database).MigrateAsync(default);
        db.ChangeTracker.Clear();
        Assert.Equal("Санкт-Петербург", (await db.Accounts.SingleAsync(a => a.Id == owner.Id)).City);
        Assert.Equal("Киров", (await db.Accounts.SingleAsync(a => a.Email == "ambiguous-city@example.test")).City);
        Assert.Equal("Старый неизвестный город", (await db.Accounts.SingleAsync(a => a.Email == "unknown-city@example.test")).City);
        Assert.Equal("Санкт-Петербург", (await db.Rides.SingleAsync()).City);
        Assert.Equal("Санкт-Петербург", (await db.LibraryRoutes.SingleAsync()).City);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Writes_normalize_city_aliases_and_filters_find_the_same_records()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = new AuthFactory { Database = database };
        app.Settings["Authentication:Store"] = "Postgres"; app.Settings["Community:Store"] = "Postgres";
        using var owner = app.Browser(); using var guest = app.Browser();
        var (session, login) = await AuthTestClient.Login(owner, "city-owner@example.test"); login.Dispose();
        var rideBody = new SaveRideRequest("Вечерняя поездка", "Едем вдоль набережной", "Питер", "У парка", app.Clock.GetUtcNow().AddDays(1), "Europe/Moscow", 20, 15, RideDifficulty.Easy);
        using var rideResponse = await AuthTestClient.Post(owner, "/api/rides", rideBody);
        Assert.True(rideResponse.IsSuccessStatusCode, await rideResponse.Content.ReadAsStringAsync());
        Assert.Equal("Санкт-Петербург", (await rideResponse.Content.ReadFromJsonAsync<RideDto>())!.City);
        var routeBody = new SaveLibraryRouteRequest("Маршрут у реки", "Дорога вдоль воды", "СПБ", RouteSurface.Mixed,
            new MapRoute("Речной маршрут", [[new(59.9, 30.3), new(59.91, 30.31)]], []));
        using var routeResponse = await AuthTestClient.Post(owner, "/api/route-library", routeBody);
        Assert.True(routeResponse.IsSuccessStatusCode, await routeResponse.Content.ReadAsStringAsync());
        Assert.Equal("Санкт-Петербург", (await routeResponse.Content.ReadFromJsonAsync<LibraryRouteDto>())!.City);
        foreach (var path in new[] { "/api/rides", "/api/route-library" })
        {
            using var result = await guest.GetAsync(path + "?city=" + Uri.EscapeDataString("питер"));
            Assert.True(result.IsSuccessStatusCode);
            Assert.Contains("Санкт-Петербург", await result.Content.ReadAsStringAsync());
        }
        using var invalid = await AuthTestClient.Post(owner, "/api/rides", rideBody with { City = "Питре" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var bootstrap = await owner.GetFromJsonAsync<BikeHub.Api.Authentication.AuthBootstrap>("/api/auth/session");
        using var profileRequest = new HttpRequestMessage(HttpMethod.Put, "/api/users/me") { Content = JsonContent.Create(new { name = "City rider", city = "санкт петербург", bio = "" }) };
        profileRequest.Headers.Add("X-CSRF-TOKEN", bootstrap!.CsrfToken);
        using var profile = await owner.SendAsync(profileRequest);
        Assert.True(profile.IsSuccessStatusCode, await profile.Content.ReadAsStringAsync());
        Assert.Equal("Санкт-Петербург", (await profile.Content.ReadFromJsonAsync<UserDto>())!.City);
        Assert.Single((await guest.GetFromJsonAsync<Page<UserDto>>("/api/search/people?q=" + Uri.EscapeDataString("питер")))!.Items);
        Assert.NotNull(session.User);
    }
}
