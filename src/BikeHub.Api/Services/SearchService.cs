using BikeHub.Application.Community;
using BikeHub.Dal;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Services;

public sealed class SearchService(IDbContextFactory<BikeHubDbContext> contexts, PostgresCommunityService community)
{
    private static Page<T> Page<T>(IQueryable<T> source, int page, int size)
    {
        page = Math.Clamp(page, 1, 1000000); size = Math.Clamp(size, 1, 50);
        return new(source.Skip((page - 1) * size).Take(size).ToArray(), source.Count(), page, size);
    }

    public Page<PostDto> Posts(string? query, int page, int size)
    {
        using var db = contexts.CreateDbContext();
        var pattern = PostgresCommunityService.Search(query);
        var ids = db.Posts.Where(p => !p.IsDeleted && !p.IsHidden && pattern != null &&
            EF.Functions.ILike(p.Title + " " + p.Content, pattern!, Constants.SearchEscapeCharacter)).Select(p => p.Id);
        return Page(community.Posts(db).Where(p => ids.Contains(p.Id)).OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id), page, size);
    }

    public Page<UserDto> People(string? query, int page, int size)
    {
        using var db = contexts.CreateDbContext();
        PostgresCommunityService.Search(query);
        var pattern = PostgresCommunityService.Search(query?.Trim().TrimStart('@'));
        var ids = db.Accounts.Where(a => !a.IsBlocked && pattern != null &&
            EF.Functions.ILike(a.DisplayName + " " + (a.Handle ?? string.Empty) + " " + a.City, pattern!, Constants.SearchEscapeCharacter)).Select(a => a.Id);
        return Page(community.Users(db).Where(u => ids.Contains(u.Id)).OrderBy(u => u.Handle).ThenBy(u => u.Id), page, size);
    }

    public Page<CommunityDto> Communities(string? query, int page, int size)
    {
        using var db = contexts.CreateDbContext();
        var pattern = PostgresCommunityService.Search(query);
        var groups = community.Groups(db, query).Where(g => pattern != null);
        return Page(groups.OrderBy(g => g.Name).ThenBy(g => g.Id), page, size);
    }
}
