using BikeHub.Dal.ReferenceData;

namespace BikeHub.Api.Services;

public static class CitySelection
{
    private const string ChooseTitle = "Выберите город из справочника. Для одноимённых городов укажите регион.";

    public static string Normalize(string? value, bool optional = false, string? existing = null)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length > CityCatalog.MaximumQueryLength)
        {
            throw new CommunityProblem(400, ChooseTitle);
        }
        if (optional && text.Length == 0)
        {
            return string.Empty;
        }
        if (CityCatalog.Resolve(text) is { } city)
        {
            return city.Label;
        }
        // An unrelated profile/content edit may retain a legacy place outside the current directory.
        if (text.Length > 0 && text == existing)
        {
            return text;
        }
        throw new CommunityProblem(400, ChooseTitle);
    }

    public static string? Filter(string? value) => CityCatalog.Resolve(value)?.Label ?? value;
}
