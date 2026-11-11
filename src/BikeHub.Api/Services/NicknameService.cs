using System.ComponentModel.DataAnnotations;
using BikeHub.Api.Authentication;
using BikeHub.Application.Community;
using BikeHub.Dal;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BikeHub.Api.Services;

public sealed record ChooseNicknameRequest([Required, StringLength(32)] string Nickname);

public sealed class NicknameService(IDbContextFactory<BikeHubDbContext> contexts, ICurrentUser current, PostgresCommunityService users)
{
    public UserDto? Find(string nickname)
    {
        nickname = Nicknames.Normalize(nickname, existing: true);
        using var db = contexts.CreateDbContext();
        return users.Users(db).SingleOrDefault(u => u.Handle == nickname);
    }
    public IReadOnlyList<UserDto> Search(string? query)
    {
        var prefix = query?.Trim().TrimStart('@').ToLowerInvariant() ?? string.Empty;
        if (prefix.Length is < 1 or > 32 || prefix.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
        {
            return [];
        }
        using var db = contexts.CreateDbContext();
        return users.Users(db).Where(u => u.Handle != string.Empty && u.Handle.StartsWith(prefix))
            .OrderBy(u => u.Handle).Take(10).ToArray();
    }
    public UserDto Choose(string nickname)
    {
        nickname = Nicknames.Normalize(nickname);
        var id = current.Id ?? throw new CommunityProblem(401, "Войдите в аккаунт.");
        using var db = contexts.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();
        db.Database.ExecuteSqlInterpolated($"SELECT pg_advisory_xact_lock(1112099655, hashtext({nickname}))");
        var account = db.Accounts.FromSqlInterpolated($"SELECT * FROM auth_accounts WHERE id = {id} FOR UPDATE").SingleOrDefault()
            ?? throw new CommunityProblem(401, "Войдите в аккаунт.");
        if (account.Handle is not null && account.Handle != nickname)
        {
            throw new CommunityProblem(409, Nicknames.ImmutableTitle);
        }
        if (db.Accounts.Any(a => a.Id != id && a.Handle == nickname))
        {
            throw new CommunityProblem(409, Nicknames.TakenTitle);
        }
        account.Handle = nickname;
        try { db.SaveChanges(); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new CommunityProblem(409, Nicknames.TakenTitle);
        }
        transaction.Commit();
        return users.GetCurrentUser();
    }
}
