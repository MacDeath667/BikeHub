using System.Net;
using System.Net.Http.Json;
using BikeHub.Api.Authentication;
using BikeHub.Api.Controllers;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Tests;

[Collection("Postgres persistence")]
public sealed class AdminRoleTests
{
    [Fact]
    public async Task Grant_is_persistent_idempotent_and_refreshes_roles_in_existing_sessions()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        using var app = new AuthFactory { Database = database };
        app.Settings["Community:Store"] = "Postgres";
        app.Settings["Authentication:Store"] = "Postgres";
        app.Settings["Moderation:ModeratorEmails:0"] = "moderator@example.test";
        using var owner = app.Browser();
        using var moderator = app.Browser();
        using var anonymous = app.Browser();
        var (member, memberResponse) = await AuthTestClient.Login(owner, "admin-owner@example.test");
        memberResponse.Dispose();
        var (_, moderatorResponse) = await AuthTestClient.Login(moderator, "moderator@example.test");
        moderatorResponse.Dispose();
        Assert.False(member.User!.IsAdmin);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/admin/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await moderator.GetAsync("/api/admin/overview")).StatusCode);

        var granted = await AdminAccountCommand.GrantAsync(database, " ADMIN-OWNER@EXAMPLE.TEST ", default);
        Assert.True(granted.Changed);
        Assert.Equal(member.User.Id, granted.AccountId);
        Assert.Equal(AccountRole.Admin, granted.Role);
        Assert.False((await AdminAccountCommand.GrantAsync(database, "admin-owner@example.test", default)).Changed);
        Assert.Equal(AccountRole.Admin, (await new PostgresAuthenticationStore(database).GetAccountAsync(granted.AccountId, default))!.Role);

        var session = (await owner.GetFromJsonAsync<AuthBootstrap>("/api/auth/session"))!;
        Assert.True(session.User!.IsAdmin);
        Assert.True(session.User.IsModerator);
        var overview = (await owner.GetFromJsonAsync<AdminOverview>("/api/admin/overview"))!;
        Assert.Equal(2, overview.Accounts);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/moderation/reports")).StatusCode);
        var csrf = (await moderator.GetFromJsonAsync<AuthBootstrap>("/api/auth/session"))!.CsrfToken;
        using var block = new HttpRequestMessage(HttpMethod.Put, $"/api/moderation/users/{granted.AccountId}/block")
        { Content = JsonContent.Create(new { blocked = true }) };
        block.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.Conflict, (await moderator.SendAsync(block)).StatusCode);

        // Issue an actual Admin cookie, then revoke the DB role without changing that cookie.
        app.Clock.Advance(TimeSpan.FromMinutes(2));
        var (adminLogin, adminResponse) = await AuthTestClient.Login(owner, "admin-owner@example.test", registering: false);
        adminResponse.Dispose();
        Assert.True(adminLogin.User!.IsAdmin);
        await using var db = database.CreateDbContext();
        await db.Accounts.Where(a => a.Id == granted.AccountId).ExecuteUpdateAsync(u => u.SetProperty(a => a.Role, AccountRole.Member));
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/admin/overview")).StatusCode);
        var demoted = (await owner.GetFromJsonAsync<AuthBootstrap>("/api/auth/session"))!;
        Assert.False(demoted.User!.IsAdmin);
        Assert.False(demoted.User.IsModerator);
    }

    [Fact]
    public async Task Registration_and_profile_updates_cannot_assign_a_role()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        using var app = new AuthFactory { Database = database };
        app.Settings["Community:Store"] = "Postgres";
        app.Settings["Authentication:Store"] = "Postgres";
        using var client = app.Browser();
        using var requested = await AuthTestClient.Post(client, "/api/auth/registration/email/challenges",
            new { email = "member@example.test", name = "Member", role = AccountRole.Admin, isAdmin = true });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var challenge = (await requested.Content.ReadFromJsonAsync<EmailCodeResponse>())!;
        var message = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/auth/development/messages/{challenge.ChallengeId}");
        using var confirmed = await AuthTestClient.Post(client, "/api/auth/email/verify",
            new { challengeId = challenge.ChallengeId, code = message.GetProperty("code").GetString() });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var session = (await confirmed.Content.ReadFromJsonAsync<AuthBootstrap>())!;
        Assert.False(session.User!.IsAdmin);
        using var profile = new HttpRequestMessage(HttpMethod.Put, "/api/users/me")
        { Content = JsonContent.Create(new { name = "Member", city = "", bio = "", role = AccountRole.Admin, isAdmin = true }) };
        profile.Headers.Add("X-CSRF-TOKEN", session.CsrfToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(profile)).StatusCode);
        Assert.False((await client.GetFromJsonAsync<AuthBootstrap>("/api/auth/session"))!.User!.IsAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/overview")).StatusCode);
    }

    [Fact]
    public async Task Operator_cannot_create_an_account_confirm_an_email_or_unblock_a_user()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        await Assert.ThrowsAsync<AdminAccountCommandException>(() => AdminAccountCommand.GrantAsync(database, "missing@example.test", default));
        await using var db = database.CreateDbContext();
        Assert.Empty(await db.Accounts.ToListAsync());
        var now = DateTimeOffset.UtcNow;
        var blocked = new AuthAccountEntity { Id = Guid.NewGuid(), Email = "blocked@example.test", DisplayName = "Blocked", CreatedAt = now, EmailConfirmedAt = now, IsBlocked = true };
        var unconfirmed = new AuthAccountEntity { Id = Guid.NewGuid(), Email = "unconfirmed@example.test", DisplayName = "Unconfirmed", CreatedAt = now };
        db.Accounts.AddRange(blocked, unconfirmed);
        await db.SaveChangesAsync();
        foreach (var account in new[] { blocked, unconfirmed })
        {
            await Assert.ThrowsAsync<AdminAccountCommandException>(() => AdminAccountCommand.GrantAsync(database, account.Email, default));
        }
        db.ChangeTracker.Clear();
        Assert.All(await db.Accounts.ToListAsync(), account => Assert.Equal(AccountRole.Member, account.Role));
        Assert.True((await db.Accounts.SingleAsync(a => a.Id == blocked.Id)).IsBlocked);
    }

    [Fact]
    public async Task Role_migration_can_be_replayed_without_removing_an_existing_assignment()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        await using var db = database.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var account = new AuthAccountEntity { Id = Guid.NewGuid(), Email = "persisted@example.test", DisplayName = "Admin", EmailConfirmedAt = now, CreatedAt = now, Role = AccountRole.Admin };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        var migration = (await db.Database.GetAppliedMigrationsAsync()).Last();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {migration}");
        await new DatabaseMigrator(database).MigrateAsync(default);
        db.ChangeTracker.Clear();
        Assert.Equal(AccountRole.Admin, (await db.Accounts.SingleAsync()).Role);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
