using System.Security.Cryptography;
using System.Text;
using BikeHub.Api.Authentication;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Services;

public sealed class MessageService(IDbContextFactory<BikeHubDbContext> contexts, ICurrentUser current, TimeProvider clock,
    PostgresCommunityService community)
{
    private const string InvalidMessageTitle = "Укажите текст сообщения и новый идентификатор отправки.";
    private const string IdempotencyTitle = "Этот идентификатор отправки уже использован для другого сообщения.";
    private const string PendingTitle = "Сначала нужно принять запрос на переписку. До этого доступно одно первое сообщение.";
    private const string DeclinedTitle = "Запрос отклонён. Новые сообщения недоступны до принятия запроса получателем.";
    private const string DecisionTitle = "Ответить на запрос может только получатель.";
    private const string RateTitle = "Слишком много сообщений. Повторите через минуту.";
    private const string RequestRateTitle = "Лимит запросов контакта достигнут. Попробуйте позднее.";
    private const string BlocksLimitTitle = "Список блокировок заполнен.";
    private const string ReadTitle = "Некорректная позиция прочтения.";
    private Guid Actor => current.Id ?? throw new CommunityProblem(401, Constants.Messages.SignInTitle);

    internal static IQueryable<PrivateConversation> Own(BikeHubDbContext db, Guid actor) => db.PrivateConversations.AsNoTracking()
        .Where(c => c.FirstUserId == actor || c.SecondUserId == actor);
    internal static IQueryable<MessageBlock> Blocks(BikeHubDbContext db, Guid actor) => db.MessageBlocks.AsNoTracking().Where(b => b.UserId == actor || b.BlockedUserId == actor);
    private static Guid? Peer(PrivateConversation conversation, Guid actor) => conversation.FirstUserId == actor ? conversation.SecondUserId : conversation.FirstUserId;
    private static bool Blocked(BikeHubDbContext db, Guid actor, Guid? peer) => db.MessageBlocks.Any(b =>
        b.UserId == actor && b.BlockedUserId == peer || b.UserId == peer && b.BlockedUserId == actor);
    internal static AuthAccountEntity[] LockAccounts(BikeHubDbContext db, params Guid[] users)
    {
        var ids = users.Distinct().ToArray();
        // All message writers use the same account-before-conversation order as profile erasure.
        return db.Accounts.FromSqlInterpolated($"SELECT * FROM auth_accounts WHERE id = ANY({ids}) ORDER BY id FOR UPDATE").ToArray();
    }
    private PrivateConversation Find(BikeHubDbContext db, Guid id) => Own(db, Actor).SingleOrDefault(c => c.Id == id)
        ?? throw new CommunityProblem(404, Constants.Messages.MissingTitle);
    private PrivateConversation Locked(BikeHubDbContext db, Guid id)
    {
        var actor = Actor; var found = Find(db, id);
        var users = LockAccounts(db, new[] { actor, Peer(found, actor) }.Where(u => u.HasValue).Select(u => u!.Value).ToArray());
        if (!users.Any(u => u.Id == actor && !u.IsBlocked)) { throw new CommunityProblem(401, Constants.Messages.SignInTitle); }
        return db.PrivateConversations.FromSqlInterpolated($"SELECT * FROM private_conversations WHERE id = {id} AND (first_user_id = {actor} OR second_user_id = {actor}) FOR UPDATE")
            .SingleOrDefault() ?? throw new CommunityProblem(404, Constants.Messages.MissingTitle);
    }
    private static MessagePeerDto PeerView(AuthAccountEntity? peer) => peer is null
        ? new(null, Constants.Messages.DeletedAccountName, null, null, false)
        : new(peer.Id, peer.DisplayName, peer.Handle, peer.AvatarId.HasValue ? Constants.MediaPathPrefix + peer.AvatarId.Value : null, !peer.IsBlocked && peer.EmailConfirmedAt != default);
    private IReadOnlyList<ConversationDto> Views(BikeHubDbContext db, IQueryable<PrivateConversation> query)
    {
        var actor = Actor;
        var rows = query.Select(c => new { c.Id, c.Status, c.RequestedById, c.LatestSequence, c.UpdatedAt,
            PeerId = c.FirstUserId == actor ? c.SecondUserId : c.FirstUserId,
            ReadSequence = c.FirstUserId == actor ? c.FirstReadSequence : c.SecondReadSequence,
            LastText = db.PrivateMessages.Where(m => m.ConversationId == c.Id).OrderByDescending(m => m.Sequence)
                .Select(m => m.IsDeleted || m.IsHidden ? null : m.Text.Substring(0, Math.Min(m.Text.Length, 120))).FirstOrDefault(),
            Unread = db.PrivateMessages.Count(m => m.ConversationId == c.Id && m.SenderId != actor && !m.IsDeleted && !m.IsHidden &&
                m.Sequence > (c.FirstUserId == actor ? c.FirstReadSequence : c.SecondReadSequence)) }).ToArray();
        var peers = rows.Where(r => r.PeerId.HasValue).Select(r => r.PeerId!.Value).Distinct().ToArray();
        var accounts = db.Accounts.AsNoTracking().Where(a => peers.Contains(a.Id)).ToDictionary(a => a.Id);
        var blocked = Blocks(db, actor).Where(b => peers.Contains(b.UserId) || peers.Contains(b.BlockedUserId)).ToArray();
        return rows.Select(r =>
        {
            var peer = r.PeerId.HasValue ? PeerView(accounts.GetValueOrDefault(r.PeerId.Value)) : PeerView(null);
            var unavailable = !peer.Available || blocked.Any(b => b.UserId == r.PeerId || b.BlockedUserId == r.PeerId);
            return new ConversationDto(r.Id, peer, r.Status, r.RequestedById == actor,
                !unavailable && r.Status == ConversationStatus.Accepted,
                !unavailable && r.RequestedById.HasValue && r.RequestedById != actor && r.Status != ConversationStatus.Accepted,
                blocked.Any(b => b.UserId == actor && b.BlockedUserId == r.PeerId), unavailable || r.Status == ConversationStatus.Declined ? 0 : r.Unread,
                r.LatestSequence, r.UpdatedAt, r.LastText);
        }).ToArray();
    }
    public ConversationDto Get(Guid id)
    {
        using var db = contexts.CreateDbContext(); return Views(db, Own(db, Actor).Where(c => c.Id == id)).SingleOrDefault()
            ?? throw new CommunityProblem(404, Constants.Messages.MissingTitle);
    }
    public MessageTargetDto Target(Guid userId)
    {
        var actor = Actor; using var db = contexts.CreateDbContext();
        if (actor == userId) { throw new CommunityProblem(404, Constants.Messages.UnavailableTitle); }
        var user = db.Accounts.AsNoTracking().SingleOrDefault(a => a.Id == userId && !a.IsBlocked && a.EmailConfirmedAt != default)
            ?? throw new CommunityProblem(404, Constants.Messages.UnavailableTitle);
        var conversation = Own(db, actor).SingleOrDefault(c => c.FirstUserId == userId || c.SecondUserId == userId);
        return new(PeerView(user), conversation?.Id, !Blocked(db, actor, userId), db.MessageBlocks.Any(b => b.UserId == actor && b.BlockedUserId == userId));
    }
    public Page<ConversationDto> List(bool requests, bool archived, int page, int size)
    {
        var actor = Actor; using var db = contexts.CreateDbContext(); page = Math.Clamp(page, 1, 1000000); size = Math.Clamp(size, 1, 50);
        var query = Own(db, actor).Where(c => !db.MessageBlocks.Any(b =>
            b.UserId == actor && (b.BlockedUserId == c.FirstUserId || b.BlockedUserId == c.SecondUserId) ||
            b.BlockedUserId == actor && (b.UserId == c.FirstUserId || b.UserId == c.SecondUserId)));
        query = archived ? query.Where(c => c.Status == ConversationStatus.Declined || (c.FirstUserId == actor ? c.FirstArchived : c.SecondArchived))
            : query.Where(c => !(c.FirstUserId == actor ? c.FirstArchived : c.SecondArchived) && (requests
                ? c.Status == ConversationStatus.Pending && c.RequestedById != actor
                : c.Status == ConversationStatus.Accepted || c.Status == ConversationStatus.Pending && c.RequestedById == actor));
        return new(Views(db, query.OrderByDescending(c => c.UpdatedAt).ThenByDescending(c => c.Id).Skip((page - 1) * size).Take(size)), query.Count(), page, size);
    }
    public int UnreadCount()
    {
        var actor = Actor; using var db = contexts.CreateDbContext();
        var conversations = Own(db, actor).Where(c => c.Status != ConversationStatus.Declined && !(c.FirstUserId == actor ? c.FirstArchived : c.SecondArchived) &&
            !db.MessageBlocks.Any(b => b.UserId == actor && (b.BlockedUserId == c.FirstUserId || b.BlockedUserId == c.SecondUserId) || b.BlockedUserId == actor && (b.UserId == c.FirstUserId || b.UserId == c.SecondUserId)));
        return db.PrivateMessages.Count(m => !m.IsDeleted && !m.IsHidden && m.SenderId != actor && db.Accounts.Any(a => a.Id == m.SenderId && !a.IsBlocked) &&
            conversations.Any(c => c.Id == m.ConversationId && m.Sequence > (c.FirstUserId == actor ? c.FirstReadSequence : c.SecondReadSequence)));
    }
    private static byte[] MessageHash(string text) => SHA256.HashData(Encoding.UTF8.GetBytes(text));
    private static string Text(string text, Guid key)
    {
        if (key == Guid.Empty || string.IsNullOrWhiteSpace(text) || text.Length > Constants.Messages.MaximumTextLength)
        {
            throw new CommunityProblem(400, InvalidMessageTitle);
        }
        return text.Trim();
    }
    private static PrivateMessageDto View(PrivateMessage message) => new(message.Id, message.SenderId, message.Sequence,
        message.IsDeleted || message.IsHidden ? null : message.Text, message.IsDeleted, message.IsHidden, message.CreatedAt);
    private PrivateMessageDto? Replay(BikeHubDbContext db, PrivateConversation conversation, string text, Guid key)
    {
        var actor = Actor; var existing = db.PrivateMessages.AsNoTracking().SingleOrDefault(m => m.SenderId == actor && m.ClientMessageId == key);
        if (existing is null) { return null; }
        if (existing.ConversationId != conversation.Id || !CryptographicOperations.FixedTimeEquals(existing.RequestHash, MessageHash(text)))
        {
            throw new CommunityProblem(409, IdempotencyTitle);
        }
        return View(existing);
    }
    private Guid AvailablePeer(BikeHubDbContext db, PrivateConversation conversation)
    {
        var actor = Actor; var peer = Peer(conversation, actor);
        if (!peer.HasValue || !db.Accounts.Any(a => a.Id == peer && !a.IsBlocked && a.EmailConfirmedAt != default) || Blocked(db, actor, peer))
        {
            throw new CommunityProblem(404, Constants.Messages.UnavailableTitle);
        }
        return peer.Value;
    }
    private PrivateMessageDto Insert(BikeHubDbContext db, PrivateConversation conversation, string text, Guid key, bool request)
    {
        var actor = Actor;
        if (Replay(db, conversation, text, key) is { } replay) { return replay; }
        var peer = AvailablePeer(db, conversation);
        if (!request && conversation.Status != ConversationStatus.Accepted)
        {
            throw new CommunityProblem(409, conversation.Status == ConversationStatus.Declined ? DeclinedTitle : PendingTitle);
        }
        var now = clock.GetUtcNow();
        if (db.PrivateMessages.Count(m => m.SenderId == actor && m.CreatedAt >= now.AddMinutes(-1)) >= Constants.Messages.MaximumMessagesPerMinute)
        {
            throw new CommunityProblem(429, RateTitle);
        }
        var read = conversation.FirstUserId == peer ? conversation.FirstReadSequence : conversation.SecondReadSequence;
        var notify = request || !db.PrivateMessages.Any(m => m.ConversationId == conversation.Id && m.SenderId == actor && m.Sequence > read && !m.IsDeleted && !m.IsHidden);
        conversation.LatestSequence++; conversation.UpdatedAt = now; conversation.FirstArchived = false; conversation.SecondArchived = false;
        var message = new PrivateMessage { Id = Guid.CreateVersion7(), ConversationId = conversation.Id, SenderId = actor, Sequence = conversation.LatestSequence,
            ClientMessageId = key, RequestHash = MessageHash(text), Text = text, CreatedAt = now };
        db.PrivateMessages.Add(message); db.SaveChanges();
        if (notify) { NotificationWriter.Add(db, peer, actor, request ? NotificationKind.MessageRequest : NotificationKind.PrivateMessage, ReportTarget.PrivateMessage, message.Id, conversation.Id, now); }
        return View(message);
    }
    public ConversationDto Start(StartConversationRequest request)
    {
        var actor = Actor; var text = Text(request.Text, request.ClientMessageId);
        using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction();
        if (actor == request.UserId) { throw new CommunityProblem(404, Constants.Messages.UnavailableTitle); }
        var users = LockAccounts(db, actor, request.UserId);
        if (!users.Any(u => u.Id == actor && !u.IsBlocked)) { throw new CommunityProblem(401, Constants.Messages.SignInTitle); }
        if (!db.Accounts.Any(a => a.Id == request.UserId && !a.IsBlocked && a.EmailConfirmedAt != default) || Blocked(db, actor, request.UserId))
        {
            throw new CommunityProblem(404, Constants.Messages.UnavailableTitle);
        }
        var conversation = db.PrivateConversations.SingleOrDefault(c => c.FirstUserId == actor && c.SecondUserId == request.UserId || c.FirstUserId == request.UserId && c.SecondUserId == actor);
        if (conversation is not null)
        {
            if (Replay(db, conversation, text, request.ClientMessageId) is null)
            {
                if (conversation.Status == ConversationStatus.Pending && conversation.RequestedById == actor)
                {
                    var first = db.PrivateMessages.OrderBy(m => m.Sequence).First(m => m.ConversationId == conversation.Id);
                    if (!CryptographicOperations.FixedTimeEquals(first.RequestHash, MessageHash(text))) { throw new CommunityProblem(409, PendingTitle); }
                }
                else { _ = Insert(db, conversation, text, request.ClientMessageId, false); }
            }
        }
        else
        {
            var now = clock.GetUtcNow();
            if (db.PrivateConversations.Count(c => c.RequestedById == actor && c.Status == ConversationStatus.Pending) >= Constants.Messages.MaximumPendingRequests ||
                db.PrivateConversations.Count(c => c.RequestedById == actor && c.CreatedAt >= now.AddDays(-1)) >= Constants.Messages.MaximumRequestsPerDay)
            {
                throw new CommunityProblem(429, RequestRateTitle);
            }
            var pair = new[] { actor, request.UserId }.Order().ToArray();
            conversation = new PrivateConversation { Id = Guid.CreateVersion7(), FirstUserId = pair[0], SecondUserId = pair[1], RequestedById = actor, Status = ConversationStatus.Pending, CreatedAt = now, UpdatedAt = now };
            db.PrivateConversations.Add(conversation); _ = Insert(db, conversation, text, request.ClientMessageId, true);
        }
        transaction.Commit(); return Get(conversation.Id);
    }
    public PrivateMessageDto Send(Guid id, SendPrivateMessageRequest request)
    {
        var text = Text(request.Text, request.ClientMessageId); using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction();
        var conversation = Locked(db, id); var message = Insert(db, conversation, text, request.ClientMessageId, false); transaction.Commit(); return message;
    }
    public ConversationDto Decide(Guid id, bool accept)
    {
        var actor = Actor; using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction(); var conversation = Locked(db, id);
        _ = AvailablePeer(db, conversation);
        if (conversation.RequestedById == actor || !conversation.RequestedById.HasValue || !accept && conversation.Status == ConversationStatus.Accepted)
        {
            throw new CommunityProblem(409, DecisionTitle);
        }
        if (accept && conversation.Status == ConversationStatus.Accepted || !accept && conversation.Status == ConversationStatus.Declined)
        {
            transaction.Commit(); return Get(id);
        }
        conversation.Status = accept ? ConversationStatus.Accepted : ConversationStatus.Declined;
        if (accept) { SetRead(conversation, actor, conversation.LatestSequence); }
        db.SaveChanges(); MarkNotificationsRead(db, id, conversation.LatestSequence); transaction.Commit(); return Get(id);
    }
    private static void SetRead(PrivateConversation conversation, Guid actor, long sequence)
    {
        if (conversation.FirstUserId == actor) { conversation.FirstReadSequence = Math.Max(conversation.FirstReadSequence, sequence); }
        else { conversation.SecondReadSequence = Math.Max(conversation.SecondReadSequence, sequence); }
    }
    private void MarkNotificationsRead(BikeHubDbContext db, Guid id, long sequence)
    {
        var actor = Actor;
        db.Notifications.Where(n => n.RecipientId == actor && n.TargetType == ReportTarget.PrivateMessage && n.ContainerId == id && n.ReadAt == null &&
            db.PrivateMessages.Any(m => m.Id == n.TargetId && m.Sequence <= sequence)).ExecuteUpdate(u => u.SetProperty(n => n.ReadAt, clock.GetUtcNow()));
    }
    public void Read(Guid id, long sequence)
    {
        var actor = Actor; using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction(); var conversation = Locked(db, id);
        if (sequence < 0 || sequence > conversation.LatestSequence) { throw new CommunityProblem(400, ReadTitle); }
        SetRead(conversation, actor, sequence); db.SaveChanges(); MarkNotificationsRead(db, id, sequence); transaction.Commit();
    }
    public void Archive(Guid id, bool archived)
    {
        var actor = Actor; using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction(); var conversation = Locked(db, id);
        if (conversation.FirstUserId == actor) { conversation.FirstArchived = archived; } else { conversation.SecondArchived = archived; }
        db.SaveChanges(); transaction.Commit();
    }
    public MessagePageDto Messages(Guid id, long? before, long? after, int size)
    {
        using var db = contexts.CreateDbContext(); var conversation = Find(db, id); size = Math.Clamp(size, 1, 50);
        if (before.HasValue && after.HasValue || before <= 0 || after < 0) { throw new CommunityProblem(400, ReadTitle); }
        var query = db.PrivateMessages.AsNoTracking().Where(m => m.ConversationId == id);
        var selected = before.HasValue ? query.Where(m => m.Sequence < before) : after.HasValue ? query.Where(m => m.Sequence > after) : query;
        var rows = (after.HasValue ? selected.OrderBy(m => m.Sequence).Take(size) : selected.OrderByDescending(m => m.Sequence).Take(size)).ToArray().OrderBy(m => m.Sequence).ToArray();
        var first = rows.Length > 0 ? rows[0].Sequence : 0; var last = rows.Length > 0 ? rows[^1].Sequence : 0;
        return new(rows.Select(View).ToArray(), rows.Length > 0 && query.Any(m => m.Sequence < first),
            rows.Length > 0 && query.Any(m => m.Sequence > last), conversation.LatestSequence);
    }
    public void DeleteMessage(Guid id, Guid messageId)
    {
        var actor = Actor; using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction(); _ = Locked(db, id);
        var message = db.PrivateMessages.SingleOrDefault(m => m.Id == messageId && m.ConversationId == id && m.SenderId == actor)
            ?? throw new CommunityProblem(404, Constants.Messages.MissingTitle);
        message.IsDeleted = true; message.Text = string.Empty; db.SaveChanges(); transaction.Commit();
    }
    public void Block(Guid peer, bool blocked)
    {
        var actor = Actor; if (peer == actor) { throw new CommunityProblem(404, Constants.Messages.UnavailableTitle); }
        using var db = contexts.CreateDbContext(); using var transaction = db.Database.BeginTransaction();
        var users = LockAccounts(db, actor, peer);
        if (!users.Any(u => u.Id == actor && !u.IsBlocked)) { throw new CommunityProblem(401, Constants.Messages.SignInTitle); }
        if (users.All(u => u.Id != peer)) { throw new CommunityProblem(404, Constants.Messages.UnavailableTitle); }
        var entry = db.MessageBlocks.SingleOrDefault(b => b.UserId == actor && b.BlockedUserId == peer);
        if (blocked && entry is null)
        {
            if (db.MessageBlocks.Count(b => b.UserId == actor) >= Constants.Messages.MaximumBlocks) { throw new CommunityProblem(409, BlocksLimitTitle); }
            db.MessageBlocks.Add(new MessageBlock { UserId = actor, BlockedUserId = peer, CreatedAt = clock.GetUtcNow() });
        }
        else if (!blocked && entry is not null) { db.MessageBlocks.Remove(entry); }
        db.SaveChanges(); transaction.Commit();
    }
    public Page<UserDto> BlockedUsers(int page, int size)
    {
        var actor = Actor; using var db = contexts.CreateDbContext(); page = Math.Clamp(page, 1, 1000000); size = Math.Clamp(size, 1, 50);
        var query = community.Users(db).Where(u => db.MessageBlocks.Any(b => b.UserId == actor && b.BlockedUserId == u.Id));
        return new(query.OrderBy(u => u.Name).ThenBy(u => u.Id).Skip((page - 1) * size).Take(size).ToArray(), query.Count(), page, size);
    }
}
