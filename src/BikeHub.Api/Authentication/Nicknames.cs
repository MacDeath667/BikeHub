using System.Text.RegularExpressions;
using BikeHub.Api.Services;

namespace BikeHub.Api.Authentication;

public static partial class Nicknames
{
    public const string TakenTitle = "Этот ник уже занят. Выберите другой.";
    public const string ImmutableTitle = "Ник выбирается один раз и не меняется.";
    private const string InvalidTitle = "Ник: от 3 до 32 латинских букв, цифр и подчёркиваний; UUID использовать нельзя.";
    [GeneratedRegex("^[a-z0-9_]{3,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
    public static string Normalize(string? value, bool existing = false)
    {
        var nickname = value?.Trim().ToLowerInvariant();
        if (nickname is null || !Pattern().IsMatch(nickname) || (!existing && nickname.Length == 32 && nickname.All(Uri.IsHexDigit)))
        {
            throw new CommunityProblem(400, InvalidTitle);
        }
        if (!existing && nickname is "admin" or "administrator" or "support" or "bikehub")
        {
            throw new CommunityProblem(400, "Этот ник зарезервирован. Выберите другой.");
        }
        return nickname;
    }
}
