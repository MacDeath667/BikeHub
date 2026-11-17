using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using BikeHub.Api.Authentication;
using BikeHub.Api.Services;
using BikeHub.Application.Community;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Tests;

[Collection("Postgres persistence")]
public sealed class PrivateMessageTests
{
    private static AuthFactory App(PersistenceTestDatabase database)
    {
        var app = new AuthFactory { Database = database };
        app.Settings["Authentication:Store"] = "Postgres"; app.Settings["Community:Store"] = "Postgres";
        app.Settings["Moderation:ModeratorEmails:0"] = "message-mod@example.test"; return app;
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

    [Fact]
    public async Task Contact_requests_gate_sending_and_every_private_read_checks_membership()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var sender = app.Browser(); using var recipient = app.Browser(); using var outsider = app.Browser(); using var guest = app.Browser();
        var (s, sl) = await AuthTestClient.Login(sender, "message-sender@example.test"); sl.Dispose();
        var (r, rl) = await AuthTestClient.Login(recipient, "message-recipient@example.test"); rl.Dispose();
        var (outside, ol) = await AuthTestClient.Login(outsider, "message-outside@example.test"); ol.Dispose();
        var key = Guid.NewGuid(); var start = new StartConversationRequest(r.User!.Id, "PRIVATE_CONTACT_REQUEST", key);
        var conversation = await Create<ConversationDto>(sender, "/api/messages/conversations", start); var path = $"/api/messages/conversations/{conversation.Id}";
        Assert.False(conversation.CanSend); Assert.True(conversation.RequestedByMe);
        Assert.Single((await recipient.GetFromJsonAsync<Page<ConversationDto>>("/api/messages/conversations?requests=true"))!.Items);
        Assert.Equal(1, await recipient.GetFromJsonAsync<int>("/api/messages/unread-count"));
        Assert.Equal(NotificationKind.MessageRequest, Assert.Single((await recipient.GetFromJsonAsync<Page<NotificationDto>>("/api/notifications"))!.Items).Kind);
        using var waiting = await AuthTestClient.Post(sender, path + "/messages", new SendPrivateMessageRequest("Второе до согласия", Guid.NewGuid())); Assert.Equal(HttpStatusCode.Conflict, waiting.StatusCode);
        using var selfAccept = await Change(sender, path + "/request", new { accept = true }); Assert.Equal(HttpStatusCode.Conflict, selfAccept.StatusCode);
        foreach (var suffix in new[] { "", "/messages" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(path + suffix)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync(path + suffix)).StatusCode);
        }
        var accepted = await Success<ConversationDto>(await Change(recipient, path + "/request", new { accept = true })); Assert.True(accepted.CanSend);
        Assert.Equal(0, await recipient.GetFromJsonAsync<int>("/api/messages/unread-count"));
        using var messagesResponse = await sender.GetAsync(path + "/messages"); Assert.True(messagesResponse.Headers.CacheControl!.NoStore);
        Assert.DoesNotContain("@example.test", await messagesResponse.Content.ReadAsStringAsync());
        var page = (await messagesResponse.Content.ReadFromJsonAsync<MessagePageDto>())!; var first = Assert.Single(page.Items); Assert.Equal(start.Text, first.Text);
        var text = "Ответ с @ник — без уведомления третьему участнику";
        var reply = await Create<PrivateMessageDto>(recipient, path + "/messages", new SendPrivateMessageRequest(text, Guid.NewGuid())); Assert.Equal(2, reply.Sequence);
        Assert.Equal(1, await sender.GetFromJsonAsync<int>("/api/messages/unread-count"));
        using var noCsrf = await sender.PutAsJsonAsync(path + "/read", new { sequence = 2 }); Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var foreignRead = await Change(outsider, path + "/read", new { sequence = 2 }); Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        using var read = await Change(sender, path + "/read", new { sequence = 2 }); Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
        using var earlier = await Change(sender, path + "/read", new { sequence = 1 }); Assert.Equal(HttpStatusCode.NoContent, earlier.StatusCode);
        Assert.Equal(0, await sender.GetFromJsonAsync<int>("/api/messages/unread-count"));
        Assert.Equal(0, await outsider.GetFromJsonAsync<int>("/api/notifications/unread-count"));
        Assert.Equal(HttpStatusCode.BadRequest, (await sender.GetAsync(path + "/messages?pageSize=51")).StatusCode);
        Assert.Equal(s.User!.Id, first.SenderId);
        using var archived = await Change(sender, path + "/archive", new { archived = true }); Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        Assert.True((await sender.GetFromJsonAsync<ConversationDto>(path))!.IsArchived);
        Assert.Empty((await sender.GetFromJsonAsync<Page<ConversationDto>>("/api/messages/conversations"))!.Items);
        Assert.Single((await recipient.GetFromJsonAsync<Page<ConversationDto>>("/api/messages/conversations"))!.Items);
        await Create<PrivateMessageDto>(recipient, path + "/messages", new SendPrivateMessageRequest("Новое сообщение возвращает диалог", Guid.NewGuid()));
        Assert.False((await sender.GetFromJsonAsync<ConversationDto>(path))!.IsArchived);
        var withdrawn = await Create<ConversationDto>(sender, "/api/messages/conversations", new StartConversationRequest(outside.User!.Id, "Запрос можно отозвать", Guid.NewGuid()));
        var withdrawal = await Success<ConversationDto>(await Change(sender, $"/api/messages/conversations/{withdrawn.Id}/request", method: HttpMethod.Delete)); Assert.Equal(ConversationStatus.Declined, withdrawal.Status);
        Assert.Empty((await outsider.GetFromJsonAsync<Page<ConversationDto>>("/api/messages/conversations?requests=true"))!.Items);
    }

    [Fact]
    public async Task Concurrent_retries_create_one_message_and_paging_does_not_skip_equal_timestamps()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var sender = app.Browser(); using var recipient = app.Browser();
        var (_, sl) = await AuthTestClient.Login(sender, "idem-sender@example.test"); sl.Dispose(); var (r, rl) = await AuthTestClient.Login(recipient, "idem-recipient@example.test"); rl.Dispose();
        var start = new StartConversationRequest(r.User!.Id, "Первый запрос", Guid.NewGuid());
        var conversation = await Create<ConversationDto>(sender, "/api/messages/conversations", start); var path = $"/api/messages/conversations/{conversation.Id}";
        Assert.Equal(conversation.Id, (await Create<ConversationDto>(sender, "/api/messages/conversations", start)).Id);
        await Success<ConversationDto>(await Change(recipient, path + "/request", new { accept = true }));
        var body = new SendPrivateMessageRequest("Сообщение ровно один раз", Guid.NewGuid());
        var replies = await Task.WhenAll(Create<PrivateMessageDto>(sender, path + "/messages", body), Create<PrivateMessageDto>(sender, path + "/messages", body));
        Assert.Equal(replies[0].Id, replies[1].Id);
        using var conflict = await AuthTestClient.Post(sender, path + "/messages", body with { Text = "Другой текст с тем же ключом" }); Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await Create<PrivateMessageDto>(sender, path + "/messages", new SendPrivateMessageRequest("Третье сообщение", Guid.NewGuid()));
        var newest = (await recipient.GetFromJsonAsync<MessagePageDto>(path + "/messages?pageSize=2"))!; Assert.Equal(new long[] { 2, 3 }, newest.Items.Select(m => m.Sequence)); Assert.True(newest.HasMoreBefore);
        var older = (await recipient.GetFromJsonAsync<MessagePageDto>(path + "/messages?pageSize=2&beforeSequence=2"))!; Assert.Equal(1, Assert.Single(older.Items).Sequence); Assert.False(older.HasMoreBefore);
        var changes = (await recipient.GetFromJsonAsync<MessagePageDto>(path + "/messages?pageSize=2&afterSequence=1"))!; Assert.Equal(new long[] { 2, 3 }, changes.Items.Select(m => m.Sequence));
        Assert.Single((await recipient.GetFromJsonAsync<Page<NotificationDto>>("/api/notifications"))!.Items, n => n.Kind == NotificationKind.PrivateMessage);
        using var removed = await Change(sender, path + "/messages/" + replies[0].Id, method: HttpMethod.Delete); Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var replay = await Create<PrivateMessageDto>(sender, path + "/messages", body); Assert.True(replay.IsDeleted); Assert.Null(replay.Text); Assert.Equal(replies[0].Id, replay.Id);
    }

    [Fact]
    public async Task Blocking_stops_both_directions_and_reported_snapshot_does_not_open_the_rest_of_the_chat_to_moderators()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var sender = app.Browser(); using var recipient = app.Browser(); using var moderator = app.Browser();
        var (s, sl) = await AuthTestClient.Login(sender, "report-sender@example.test"); sl.Dispose(); var (r, rl) = await AuthTestClient.Login(recipient, "report-recipient@example.test"); rl.Dispose();
        var (_, ml) = await AuthTestClient.Login(moderator, "message-mod@example.test"); ml.Dispose();
        var conversation = await Create<ConversationDto>(sender, "/api/messages/conversations", new StartConversationRequest(r.User!.Id, "OTHER_PRIVATE_TEXT", Guid.NewGuid())); var path = $"/api/messages/conversations/{conversation.Id}";
        await Success<ConversationDto>(await Change(recipient, path + "/request", new { accept = true }));
        var message = await Create<PrivateMessageDto>(sender, path + "/messages", new SendPrivateMessageRequest("REPORTED_PRIVATE_TEXT", Guid.NewGuid()));
        using var report = await AuthTestClient.Post(recipient, path + $"/messages/{message.Id}/reports", new { reason = "Оскорбление в личной переписке" }); Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        using var ownReport = await AuthTestClient.Post(sender, path + $"/messages/{message.Id}/reports", new { reason = "Нельзя жаловаться самому" }); Assert.Equal(HttpStatusCode.BadRequest, ownReport.StatusCode);
        using var ordinaryReport = await AuthTestClient.Post(recipient, "/api/reports", new { targetType = 14, targetId = message.Id, reason = "Не публичный материал" }); Assert.Equal(HttpStatusCode.NotFound, ordinaryReport.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await moderator.GetAsync(path + "/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sender.GetAsync("/api/moderation/messages/reports")).StatusCode);
        using var removed = await Change(sender, path + "/messages/" + message.Id, method: HttpMethod.Delete); Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var reportQueue = await moderator.GetAsync("/api/moderation/messages/reports"); var body = await reportQueue.Content.ReadAsStringAsync(); Assert.Contains("REPORTED_PRIVATE_TEXT", body); Assert.DoesNotContain("OTHER_PRIVATE_TEXT", body); Assert.DoesNotContain("@example.test", body);
        var reportDto = Assert.Single((await reportQueue.Content.ReadFromJsonAsync<Page<PrivateMessageReportDto>>())!.Items);
        using var block = await Change(recipient, "/api/messages/blocks/" + s.User!.Id); Assert.Equal(HttpStatusCode.NoContent, block.StatusCode);
        Assert.True((await recipient.GetFromJsonAsync<ConversationDto>(path))!.BlockedByMe); Assert.False((await sender.GetFromJsonAsync<ConversationDto>(path))!.CanSend);
        foreach (var client in new[] { sender, recipient })
        {
            using var stopped = await AuthTestClient.Post(client, path + "/messages", new SendPrivateMessageRequest("После блокировки", Guid.NewGuid())); Assert.Equal(HttpStatusCode.NotFound, stopped.StatusCode);
        }
        Assert.Equal(0, await recipient.GetFromJsonAsync<int>("/api/messages/unread-count"));
        Assert.Single((await recipient.GetFromJsonAsync<Page<UserDto>>("/api/messages/blocks"))!.Items);
        using var unblock = await Change(recipient, "/api/messages/blocks/" + s.User.Id, method: HttpMethod.Delete); Assert.Equal(HttpStatusCode.NoContent, unblock.StatusCode);
        await Create<PrivateMessageDto>(sender, path + "/messages", new SendPrivateMessageRequest("После разблокировки", Guid.NewGuid()));
        using var resolved = await AuthTestClient.Post(moderator, $"/api/moderation/messages/reports/{reportDto.Id}/resolve", new { resolution = "Проверено модератором" }); Assert.Equal(HttpStatusCode.NoContent, resolved.StatusCode);
        Assert.Empty((await moderator.GetFromJsonAsync<Page<PrivateMessageReportDto>>("/api/moderation/messages/reports"))!.Items);
    }

    [Fact]
    public async Task Sending_limits_do_not_break_replays_and_reset_after_the_window()
    {
        await using var database = await PersistenceTestDatabase.CreateAsync(); await new DatabaseMigrator(database).MigrateAsync(default);
        await using var app = App(database); using var sender = app.Browser(); using var recipient = app.Browser();
        var (s, sl) = await AuthTestClient.Login(sender, "rate-sender@example.test"); sl.Dispose(); var (r, rl) = await AuthTestClient.Login(recipient, "rate-recipient@example.test"); rl.Dispose();
        var start = new StartConversationRequest(r.User!.Id, "Первый запрос", Guid.NewGuid()); var conversation = await Create<ConversationDto>(sender, "/api/messages/conversations", start); var path = $"/api/messages/conversations/{conversation.Id}";
        await Success<ConversationDto>(await Change(recipient, path + "/request", new { accept = true }));
        using (var db = database.CreateDbContext())
        {
            await db.PrivateConversations.Where(c => c.Id == conversation.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.LatestSequence, 60));
            for (var sequence = 2; sequence <= 60; sequence++)
            {
                db.PrivateMessages.Add(new PrivateMessage { Id = Guid.CreateVersion7(), ConversationId = conversation.Id, SenderId = s.User!.Id, Sequence = sequence, ClientMessageId = Guid.NewGuid(), RequestHash = SHA256.HashData(Encoding.UTF8.GetBytes("Seed message")), Text = "Seed message", CreatedAt = app.Clock.GetUtcNow() });
            }
            await db.SaveChangesAsync();
        }
        using var limited = await AuthTestClient.Post(sender, path + "/messages", new SendPrivateMessageRequest("Лимит", Guid.NewGuid())); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var replay = await Create<PrivateMessageDto>(sender, path + "/messages", new SendPrivateMessageRequest(start.Text, start.ClientMessageId)); Assert.Equal(1, replay.Sequence);
        app.Clock.Advance(TimeSpan.FromSeconds(61));
        var sent = await Create<PrivateMessageDto>(sender, path + "/messages", new SendPrivateMessageRequest("После окна", Guid.NewGuid())); Assert.Equal(61, sent.Sequence);
    }
}
