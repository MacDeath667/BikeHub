using BikeHub.Api.Authentication;
using BikeHub.Api.Services;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BikeHub.Api.Tests;

[Collection("Postgres persistence")]
public sealed class PersistentCommunityTests
{
    private sealed class Viewer(AuthAccount? account) : ICurrentUser
    {
        public AuthAccount? Account { get; set; } = account;
        public Guid? Id => Account?.Id;
    }

    [Fact]
    public async Task Content_survives_service_restart_and_ownership_reactions_and_moderation_are_enforced()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync();
        await new DatabaseMigrator(database).MigrateAsync(default);
        var now = DateTimeOffset.UtcNow;
        var owner = new AuthAccount(Guid.NewGuid(), "owner@example.test", "Owner", now);
        var reader = new AuthAccount(Guid.NewGuid(), "reader@example.test", "Reader", now);
        var moderator = new AuthAccount(Guid.NewGuid(), "moderator@example.test", "Moderator", now);
        await using (var db = database.CreateDbContext())
        {
            db.Accounts.AddRange(new[] { owner, reader, moderator }.Select(a => new AuthAccountEntity
            { Id = a.Id, Email = a.Email, DisplayName = a.DisplayName, EmailConfirmedAt = now, CreatedAt = now }));
            await db.SaveChangesAsync();
        }
        var viewer = new Viewer(owner);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Moderation:ModeratorEmails:0"] = moderator.Email }).Build();
        PostgresCommunityService Service() => new(database, viewer, TimeProvider.System, config);
        var service = Service();
        Assert.Null(service.GetOverview().FeaturedBike);
        var bike = service.CreateBike(new("My bike", "Brand", BikeCategory.Gravel, 2024, 120, "Description"));
        var post = service.CreatePost(new(bike.Id, "First ride", "A long enough ride report #cycling", "ride"))!;
        var comment = service.AddComment(post.Id, new("First comment"))!;
        service.UpdateProfile(new("Updated owner", "updated_owner", "Москва", "Bio"));
        service = Service(); // No shared memory or retained DbContext.
        Assert.Equal("Updated owner", service.GetPost(post.Id)!.Author.Name);
        Assert.Single(service.GetFeed(null, null, "First", 1, 10).Items);
        Assert.Single(service.GetBikes("Brand", BikeCategory.Gravel, owner.Id, 1, 10).Items);
        Assert.Single(service.GetHistory(bike.Id, 1, 10)!.Items);
        Assert.Single(service.GetComments(post.Id, 1, 10)!.Items);
        Assert.Contains("cycling", service.GetOverview().Tags);
        service.UpdatePost(post.Id, new(bike.Id, "Edited ride", "A different long ride report", "ride"));
        service.UpdateBike(bike.Id, new("Edited bike", "Brand", BikeCategory.City, 2024, 123, "Updated"));
        service.UpdateComment(post.Id, comment.Id, new("Edited comment"));
        viewer.Account = reader;
        Assert.Equal(403, Assert.Throws<CommunityProblem>(() => service.DeletePost(post.Id)).Status);
        Assert.Equal(403, Assert.Throws<CommunityProblem>(() => service.UpdateComment(post.Id, comment.Id, new("Forged"))).Status);
        Assert.Null(service.CreatePost(new(bike.Id, "Forged ride", "A long enough report", "ride")));
        service.SetLike(post.Id, true); service.SetLike(post.Id, true);
        Assert.Equal(1, service.GetPost(post.Id)!.Likes);
        service.SetBookmark(post.Id, true);
        Assert.Single(service.GetFeed("saved", null, null, 1, 10).Items);
        service.SetFollowing(owner.Id, true);
        Assert.Single(service.GetFeed("following", null, null, 1, 10).Items);
        var group = service.GetCommunities(null)[0];
        service.SetMembership(group.Id, true); service.SetMembership(group.Id, true);
        Assert.Equal(1, service.GetCommunities(null).Single(g => g.Id == group.Id).Members);
        var report = service.Report(new(ReportTarget.Post, post.Id, "Please review this post"));
        Assert.Equal(report.Id, service.Report(new(ReportTarget.Post, post.Id, "Duplicate report")).Id);
        Assert.Equal(403, Assert.Throws<CommunityProblem>(() => service.Reports(false, 1, 20)).Status);
        viewer.Account = moderator;
        Assert.Single(service.Reports(false, 1, 20).Items);
        service.SetVisibility(ReportTarget.Post, post.Id, true);
        Assert.Null(service.GetPost(post.Id));
        service.SetVisibility(ReportTarget.Post, post.Id, false);
        service.ResolveReport(report.Id, "Reviewed");
        Assert.Empty(service.Reports(false, 1, 20).Items);
        service.BlockUser(owner.Id, true);
        Assert.Null(service.GetBike(bike.Id));
        service.BlockUser(owner.Id, false);
        viewer.Account = owner;
        service.DeleteComment(post.Id, comment.Id);
        Assert.Empty(service.GetComments(post.Id, 1, 10)!.Items);
        service.DeleteBike(bike.Id);
        Assert.Null(service.GetPost(post.Id));
        Assert.Empty(service.GetFeed(null, null, null, 1, 10).Items);
    }
}
