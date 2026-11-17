using BikeHub.Api.Authentication;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BikeHub.Api.Services;

public sealed class AccountDeletionService(IDbContextFactory<BikeHubDbContext> contexts, OtpSecurity security,
    TimeProvider clock, IOptions<AuthOptions> options)
{
    private const string InvalidCode = "Код удаления неверный или истёк.";
    public async Task DeleteAsync(AuthSession session, VerifyEmailCode request, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var challenge = await db.EmailChallenges.FromSqlInterpolated($"SELECT * FROM auth_email_challenges WHERE id = {request.ChallengeId} FOR UPDATE").SingleOrDefaultAsync(ct);
        var binding = security.Hash(Constants.BrowserBindingPurpose, session.Id.ToString("N"));
        if (challenge is null || challenge.Purpose != AuthenticationPurpose.AccountDeletion || challenge.Email != session.Account.Email ||
            challenge.Consumed || !challenge.Delivered || challenge.ExpiresAt <= clock.GetUtcNow() ||
            challenge.Attempts >= options.Value.MaxAttempts || !OtpSecurity.EqualsHash(challenge.BindingHash, binding))
        {
            throw new CommunityProblem(400, InvalidCode);
        }
        challenge.Attempts++;
        await db.SaveChangesAsync(ct);
        if (!OtpSecurity.EqualsHash(challenge.CodeHash, security.HashCode(request.ChallengeId, request.Code)))
        {
            await transaction.CommitAsync(ct);
            throw new CommunityProblem(400, InvalidCode);
        }
        var id = session.Account.Id;
        // Account lock serializes erasure with concurrent FK inserts (uploads, posts, memberships).
        await db.Accounts.FromSqlInterpolated($"SELECT * FROM auth_accounts WHERE id = {id} FOR UPDATE").SingleAsync(ct);
        var bikes = db.Bikes.Where(b => b.OwnerId == id).Select(b => b.Id);
        var posts = db.Posts.Where(p => p.AuthorId == id || bikes.Contains(p.BikeId)).Select(p => p.Id);
        var entries = db.GroupEntries.Where(e => e.AuthorId == id).Select(e => e.Id);
        var rides = db.Rides.Where(r => r.OwnerId == id).Select(r => r.Id);
        var routes = db.LibraryRoutes.Where(r => r.OwnerId == id).Select(r => r.Id);
        var removed = await bikes.Concat(posts).Concat(routes).Concat(db.RouteReviews.Where(r => r.AuthorId == id || routes.Contains(r.RouteId)).Select(r => r.Id)).Concat(rides).Concat(db.RideChanges.Where(c => rides.Contains(c.RideId)).Select(c => c.Id)).Concat(entries).Concat(db.Comments.Where(c => c.AuthorId == id || posts.Contains(c.PostId)).Select(c => c.Id))
            .Concat(db.GroupReplies.Where(r => r.AuthorId == id || entries.Contains(r.EntryId)).Select(r => r.Id)).ToArrayAsync(ct);
        await db.Reports.Where(r => r.ReporterId == id || r.TargetId == id || removed.Contains(r.TargetId)).ExecuteDeleteAsync(ct);
        await db.Reports.Where(r => r.ResolvedBy == id).ExecuteUpdateAsync(u => u.SetProperty(r => r.ResolvedBy, (Guid?)null), ct);
        await db.Relations.Where(r => r.UserId == id || r.TargetId == id || removed.Contains(r.TargetId)).ExecuteDeleteAsync(ct);
        await db.Notifications.Where(n => n.RecipientId == id || n.ActorId == id || n.TargetId == id || removed.Contains(n.TargetId)).ExecuteDeleteAsync(ct);
        await db.Drafts.Where(d => d.OwnerId == id).ExecuteDeleteAsync(ct);
        await db.PrivateMessageReports.Where(r => r.ReporterId == id || r.ReportedUserId == id).ExecuteDeleteAsync(ct);
        await db.PrivateMessages.Where(m => m.SenderId == id).ExecuteDeleteAsync(ct);
        await db.RouteReviews.Where(r => r.AuthorId == id).ExecuteDeleteAsync(ct);
        await db.SavedRoutes.Where(r => r.UserId == id).ExecuteDeleteAsync(ct);
        await db.RouteCollections.Where(c => c.OwnerId == id).ExecuteDeleteAsync(ct);
        await db.LibraryRoutes.Where(r => r.OwnerId == id).ExecuteDeleteAsync(ct);
        await db.RideParticipants.Where(p => p.UserId == id).ExecuteDeleteAsync(ct);
        await db.Rides.Where(r => r.OwnerId == id).ExecuteDeleteAsync(ct);
        await db.Comments.Where(c => c.AuthorId == id || posts.Contains(c.PostId)).ExecuteDeleteAsync(ct);
        await db.Posts.Where(p => p.AuthorId == id || bikes.Contains(p.BikeId)).ExecuteDeleteAsync(ct);
        await db.Bikes.Where(b => b.OwnerId == id).ExecuteDeleteAsync(ct);
        await db.GroupReplies.Where(r => r.AuthorId == id || entries.Contains(r.EntryId)).ExecuteDeleteAsync(ct);
        await db.GroupEntries.Where(e => e.AuthorId == id).ExecuteDeleteAsync(ct);
        var images = await db.Media.Where(m => m.OwnerId == id).Select(m => m.Id).ToArrayAsync(ct);
        await db.Communities.Where(g => g.OwnerId == id).ExecuteUpdateAsync(u => u.SetProperty(g => g.OwnerId, (Guid?)null), ct);
        await db.Communities.Where(g => g.ImageId != null && images.Contains(g.ImageId.Value))
            .ExecuteUpdateAsync(u => u.SetProperty(g => g.ImageId, (Guid?)null), ct);
        await db.Accounts.Where(a => a.Id == id).ExecuteUpdateAsync(u => u.SetProperty(a => a.AvatarId, (Guid?)null), ct);
        await db.Media.Where(m => m.OwnerId == id).ExecuteDeleteAsync(ct);
        await db.EmailChallenges.Where(c => c.Email == session.Account.Email).ExecuteDeleteAsync(ct);
        // Challenge was deleted directly; detach it before saving the durable file-deletion jobs.
        db.ChangeTracker.Clear();
        db.MediaDeletions.AddRange(images.Select(image => new MediaDeletion { Id = image }));
        await db.SaveChangesAsync(ct);
        await db.Sessions.Where(s => s.AccountId == id).ExecuteDeleteAsync(ct);
        await db.Accounts.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
        await db.PrivateConversations.Where(c => c.FirstUserId == null && c.SecondUserId == null).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
