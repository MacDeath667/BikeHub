using BikeHub.Dal;
using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Api.Authentication;

// Operator-only process command. Public registration and profile endpoints never accept a role.
public static class AdminAccountCommand
{
    private const string Usage = "Usage: dotnet BikeHub.Api.dll --grant-admin <verified-account-email>";
    private const string UnavailableAccount = "An existing, confirmed and unblocked account is required. No account was changed.";

    public static string ParseEmail(string[] args)
    {
        if (args.Length != 2 || args[0] != Constants.GrantAdminArgument || OtpSecurity.NormalizeEmail(args[1]) is not { } email)
        {
            throw new ArgumentException(Usage);
        }
        return email;
    }

    public static async Task<GrantAdminResult> GrantAsync(IDbContextFactory<BikeHubDbContext> contexts, string email,
        CancellationToken cancellationToken)
    {
        var normalized = OtpSecurity.NormalizeEmail(email) ?? throw new ArgumentException(Usage);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var account = await db.Accounts.FromSqlInterpolated($"SELECT * FROM auth_accounts WHERE email = {normalized} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (account is null || account.IsBlocked || account.EmailConfirmedAt == default)
        {
            throw new AdminAccountCommandException(UnavailableAccount);
        }
        var changed = account.Role != AccountRole.Admin;
        account.Role = AccountRole.Admin;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(account.Id, account.Role, changed);
    }
}

public sealed record GrantAdminResult(Guid AccountId, AccountRole Role, bool Changed);
public sealed class AdminAccountCommandException(string message) : Exception(message);
