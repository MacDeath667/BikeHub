using System.Text.RegularExpressions;
using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Services;

internal static class NotificationWriter
{
    private const int MaximumMentions = 10;
    private static readonly Regex Mention = new(@"(?<![\p{L}\p{N}_@])@([a-z0-9_]{3,32})(?![a-z0-9_])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    // Call inside the same transaction as the source mutation, after the source is saved.
    internal static void Add(BikeHubDbContext db, Guid recipient, Guid actor, NotificationKind kind,
        ReportTarget targetType, Guid target, Guid? container, DateTimeOffset createdAt)
    {
        if (recipient == actor || !db.Accounts.Any(a => a.Id == recipient && !a.IsBlocked))
        {
            return;
        }
        var id = Guid.CreateVersion7();
        db.Database.ExecuteSqlInterpolated($"""
            INSERT INTO notifications (id, recipient_id, actor_id, kind, target_type, target_id, container_id, created_at)
            VALUES ({id}, {recipient}, {actor}, {(int)kind}, {(int)targetType}, {target}, {container}, {createdAt})
            ON CONFLICT (recipient_id, actor_id, kind, target_type, target_id) DO NOTHING
            """);
    }

    internal static void Mentions(BikeHubDbContext db, string content, Guid actor, ReportTarget targetType,
        Guid target, Guid? container, DateTimeOffset createdAt, Guid? directRecipient = null)
    {
        var handles = Mention.Matches(content).Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Distinct().Take(MaximumMentions).ToArray();
        if (handles.Length == 0)
        {
            return;
        }
        var recipients = db.Accounts.Where(a => !a.IsBlocked && a.Handle != null && handles.Contains(a.Handle)
            && a.Id != actor && a.Id != directRecipient).Select(a => a.Id).ToArray();
        foreach (var recipient in recipients)
        {
            Add(db, recipient, actor, NotificationKind.Mention, targetType, target, container, createdAt);
        }
    }
}
