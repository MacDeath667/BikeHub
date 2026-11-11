using System.Net;
using System.Net.Http.Json;
using BikeHub.Api.Authentication;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BikeHub.Api.Tests;

[Collection("Postgres persistence")]
public sealed class NicknameTests
{
    private static AuthFactory App(PersistenceTestDatabase database)
    {
        var app = new AuthFactory { Database = database };
        app.Settings["Authentication:Store"] = "Postgres"; app.Settings["Community:Store"] = "Postgres";
        return app;
    }
    private static async Task<(EmailCodeResponse Challenge, string Code)> Request(HttpClient browser, string email, string nickname)
    {
        using var result = await AuthTestClient.Post(browser, "/api/auth/registration/email/challenges", new { email, nickname, name = "Public name" });
        Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
        var challenge = (await result.Content.ReadFromJsonAsync<EmailCodeResponse>())!;
        var code = await browser.GetFromJsonAsync<Code>("/api/auth/development/messages/" + challenge.ChallengeId);
        return (challenge, code!.Value);
    }
    [Fact]
    public async Task Nickname_is_required_unique_case_insensitive_immutable_and_resolvable_without_private_fields()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var owner = app.Browser(); using var other = app.Browser();
        using var missing = await AuthTestClient.Post(owner, "/api/auth/registration/email/challenges", new { email = "missing-nick@example.test", nickname = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var uuid = await AuthTestClient.Post(owner, "/api/auth/registration/email/challenges", new { email = "uuid-nick@example.test", nickname = Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.BadRequest, uuid.StatusCode);
        var (challenge, code) = await Request(owner, "rider@example.test", "Gravel_Rider");
        using var verified = await AuthTestClient.Post(owner, "/api/auth/email/verify", new { challengeId = challenge.ChallengeId, code });
        var auth = (await verified.Content.ReadFromJsonAsync<AuthBootstrap>())!;
        Assert.Equal("gravel_rider", auth.User!.Handle);
        using var duplicate = await AuthTestClient.Post(other, "/api/auth/registration/email/challenges", new { email = "other-rider@example.test", nickname = "GRAVEL_RIDER" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var found = await other.GetFromJsonAsync<UserDto>("/api/users/by-nickname/Gravel_Rider"); Assert.Equal(auth.User.Id, found!.Id);
        var search = await other.GetFromJsonAsync<UserDto[]>("/api/users/nicknames?q=gravel_"); Assert.Equal(auth.User.Id, Assert.Single(search!).Id);
        Assert.DoesNotContain("@example.test", await other.GetStringAsync("/api/users/nicknames?q=gravel"));
        using var rename = await AuthTestClient.Post(owner, "/api/users/me/nickname", new { nickname = "another_rider" });
        Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);
        var session = await owner.GetFromJsonAsync<AuthBootstrap>("/api/auth/session");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/users/me") { Content = JsonContent.Create(new { name = "Changed display name", handle = "another_rider", city = "", bio = "" }) };
        request.Headers.Add("X-CSRF-TOKEN", session!.CsrfToken);
        using var forged = await owner.SendAsync(request); Assert.Equal(HttpStatusCode.Conflict, forged.StatusCode);
        Assert.Equal("gravel_rider", (await owner.GetFromJsonAsync<AuthBootstrap>("/api/auth/session"))!.User!.Handle);
    }
    [Fact]
    public async Task Concurrent_confirmations_cannot_assign_the_same_nickname_to_two_accounts()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var first = app.Browser(); using var second = app.Browser();
        var a = await Request(first, "first-race@example.test", "race_rider");
        var b = await Request(second, "second-race@example.test", "RACE_RIDER");
        var results = await Task.WhenAll(AuthTestClient.Post(first, "/api/auth/email/verify", new { challengeId = a.Challenge.ChallengeId, code = a.Code }),
            AuthTestClient.Post(second, "/api/auth/email/verify", new { challengeId = b.Challenge.ChallengeId, code = b.Code }));
        try
        {
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
            await using var db = await database.CreateDbContextAsync(); Assert.Equal(1, await db.Accounts.CountAsync(a => a.Handle == "race_rider"));
        }
        finally { foreach (var result in results) { result.Dispose(); } }
    }
    [Fact]
    public async Task Migration_removes_only_uuid_fallbacks_and_existing_account_can_choose_once()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        var id = Guid.NewGuid(); var custom = Guid.NewGuid();
        await using (var db = await database.CreateDbContextAsync())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261111041945_PostRoutes");
            db.Accounts.AddRange(new AuthAccountEntity { Id = id, Email = "legacy-nick@example.test", DisplayName = "Legacy", Handle = id.ToString("N"), EmailConfirmedAt = DateTimeOffset.UtcNow },
                new AuthAccountEntity { Id = custom, Email = "custom-nick@example.test", DisplayName = "Custom", Handle = "custom_nick", EmailConfirmedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(); await db.Database.MigrateAsync();
            Assert.Null((await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == id)).Handle);
            Assert.Equal("custom_nick", (await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == custom)).Handle);
        }
        await using var app = App(database); using var browser = app.Browser();
        var (session, login) = await AuthTestClient.Login(browser, "legacy-nick@example.test", registering: false); login.Dispose();
        Assert.Equal(string.Empty, session.User!.Handle);
        using var chosen = await AuthTestClient.Post(browser, "/api/users/me/nickname", new { nickname = "legacy_rider" });
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
        Assert.Equal(id, (await chosen.Content.ReadFromJsonAsync<UserDto>())!.Id);
        using var rename = await AuthTestClient.Post(browser, "/api/users/me/nickname", new { nickname = "new_nickname" });
        Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);
    }
    private sealed record Code([property: System.Text.Json.Serialization.JsonPropertyName("code")] string Value);
}
