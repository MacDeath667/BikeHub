using BikeHub.Api.Authentication;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Services;

public sealed class MessageModerationService(IDbContextFactory<BikeHubDbContext> contexts, ICurrentUser current,
    PostgresCommunityService community, TimeProvider clock)
{
    private const string OwnMessageTitle = "Пожаловаться можно на полученное сообщение.";
    private const string ModeratorTitle = "Необходимы права модератора.";
    private const string ReportRateTitle = "Лимит жалоб на сегодня достигнут. Заблокируйте отправителя, чтобы прекратить сообщения.";
    private Guid Actor => current.Id ?? throw new CommunityProblem(401, Constants.Messages.SignInTitle);
    private void Moderator()
    {
        if (!community.IsModerator) { throw new CommunityProblem(403, ModeratorTitle); }
    }
    public Guid Report(Guid conversationId, Guid messageId, PrivateMessageReportRequest request)
    {
        var actor = Actor; using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction();
        var conversation = MessageService.Own(db, actor).SingleOrDefault(c => c.Id == conversationId) ?? throw new CommunityProblem(404, Constants.Messages.MissingTitle);
        var users = MessageService.LockAccounts(db, new[] { actor, conversation.FirstUserId, conversation.SecondUserId }.Where(u => u.HasValue).Select(u => u!.Value).ToArray());
        if (!users.Any(u => u.Id == actor && !u.IsBlocked)) { throw new CommunityProblem(401, Constants.Messages.SignInTitle); }
        var message = db.PrivateMessages.AsNoTracking().SingleOrDefault(m => m.Id == messageId && m.ConversationId == conversationId && !m.IsDeleted && !m.IsHidden)
            ?? throw new CommunityProblem(404, Constants.Messages.MissingTitle);
        if (message.SenderId == actor || !message.SenderId.HasValue) { throw new CommunityProblem(400, OwnMessageTitle); }
        var existing = db.PrivateMessageReports.SingleOrDefault(r => r.ReporterId == actor && r.MessageId == messageId && r.ResolvedAt == null);
        if (existing is not null) { transaction.Commit(); return existing.Id; }
        if (db.PrivateMessageReports.Count(r => r.ReporterId == actor && r.CreatedAt >= clock.GetUtcNow().AddDays(-1)) >= Constants.Messages.MaximumReportsPerDay)
        {
            throw new CommunityProblem(429, ReportRateTitle);
        }
        var report = new PrivateMessageReport { Id = Guid.CreateVersion7(), ReporterId = actor, ReportedUserId = message.SenderId,
            ConversationId = conversationId, MessageId = message.Id, MessageText = message.Text, Reason = request.Reason.Trim(), CreatedAt = clock.GetUtcNow() };
        db.PrivateMessageReports.Add(report); db.SaveChanges(); transaction.Commit(); return report.Id;
    }
    public Page<PrivateMessageReportDto> Reports(int page, int size)
    {
        Moderator(); using var db = contexts.CreateDbContext(); page = Math.Clamp(page, 1, 1000000); size = Math.Clamp(size, 1, 50);
        var query = db.PrivateMessageReports.AsNoTracking().Where(r => r.ResolvedAt == null);
        var rows = query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Skip((page - 1) * size).Take(size).ToArray();
        var ids = rows.SelectMany(r => new[] { (Guid?)r.ReporterId, r.ReportedUserId }).Where(u => u.HasValue).Select(u => u!.Value).Distinct().ToArray();
        var users = db.Accounts.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionary(a => a.Id);
        var messageIds = rows.Where(r => r.MessageId.HasValue).Select(r => r.MessageId!.Value).Distinct().ToArray();
        var available = db.PrivateMessages.Where(m => messageIds.Contains(m.Id) && !m.IsDeleted).Select(m => m.Id).ToHashSet();
        MessagePeerDto User(Guid? id) => id.HasValue && users.TryGetValue(id.Value, out var user)
            ? new(user.Id, user.DisplayName, user.Handle, user.AvatarId.HasValue ? Constants.MediaPathPrefix + user.AvatarId.Value : null, !user.IsBlocked)
            : new(null, Constants.Messages.DeletedAccountName, null, null, false);
        return new(rows.Select(r => new PrivateMessageReportDto(r.Id, r.MessageId, User(r.ReporterId), User(r.ReportedUserId), r.MessageText, r.Reason, r.CreatedAt,
            r.MessageId.HasValue && available.Contains(r.MessageId.Value))).ToArray(), query.Count(), page, size);
    }
    public void Resolve(Guid id, string resolution)
    {
        Moderator(); var actor = Actor; using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction();
        _ = MessageService.LockAccounts(db, actor);
        var report = db.PrivateMessageReports.FromSqlInterpolated($"SELECT * FROM private_message_reports WHERE id = {id} FOR UPDATE").SingleOrDefault() ?? throw new CommunityProblem(404, Constants.Messages.MissingTitle);
        if (report.ResolvedAt is null) { report.ResolvedAt = clock.GetUtcNow(); report.ResolvedById = actor; report.Resolution = resolution.Trim(); db.SaveChanges(); }
        transaction.Commit();
    }
    public void Visibility(Guid messageId, bool hidden)
    {
        Moderator(); using var db = contexts.CreateDbContext();
        if (!db.PrivateMessageReports.Any(r => r.MessageId == messageId) || db.PrivateMessages.Where(m => m.Id == messageId && !m.IsDeleted)
                .ExecuteUpdate(u => u.SetProperty(m => m.IsHidden, hidden)) == 0)
        {
            throw new CommunityProblem(404, Constants.Messages.MissingTitle);
        }
    }
}
