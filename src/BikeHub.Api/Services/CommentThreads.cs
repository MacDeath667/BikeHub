using BikeHub.Application.Community;
using BikeHub.Dal;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Services;

internal static class CommentThreads
{
    internal const string MissingReplyTitle = "Комментарий для ответа недоступен.";

    internal static IQueryable<CommentDto> Posts(BikeHubDbContext db, PostgresCommunityService community, Guid containerId) =>
        from c in db.Comments.AsNoTracking()
        join author in community.Users(db) on c.AuthorId equals author.Id
        where c.PostId == containerId && !c.IsDeleted && !c.IsHidden
        select new CommentDto { Id = c.Id, Author = author, Text = c.Text, CreatedAt = c.CreatedAt, ReplyToId = c.ReplyToId,
            ReplyTo = (from parent in db.Comments
             join a in db.Accounts on parent.AuthorId equals a.Id
             where parent.Id == c.ReplyToId && parent.PostId == c.PostId && !parent.IsDeleted && !parent.IsHidden && !a.IsBlocked
             select new ReplyContextDto { Id = parent.Id, AuthorName = a.DisplayName, AuthorHandle = a.Handle ?? string.Empty, Text = parent.Text }).FirstOrDefault() };

    internal static IQueryable<CommentDto> Groups(BikeHubDbContext db, PostgresCommunityService community, Guid containerId) =>
        from r in db.GroupReplies.AsNoTracking()
        join author in community.Users(db) on r.AuthorId equals author.Id
        where r.EntryId == containerId && !r.IsDeleted && !r.IsHidden
        select new CommentDto { Id = r.Id, Author = author, Text = r.Text, CreatedAt = r.CreatedAt, ReplyToId = r.ReplyToId,
            ReplyTo = (from parent in db.GroupReplies
             join a in db.Accounts on parent.AuthorId equals a.Id
             where parent.Id == r.ReplyToId && parent.EntryId == r.EntryId && !parent.IsDeleted && !parent.IsHidden && !a.IsBlocked
             select new ReplyContextDto { Id = parent.Id, AuthorName = a.DisplayName, AuthorHandle = a.Handle ?? string.Empty, Text = parent.Text }).FirstOrDefault() };
}
