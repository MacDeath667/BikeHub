using System.Text;
using System.Text.Json;

namespace BikeHub.Dal.ReferenceData;

public sealed record CityOption(Guid Id, string Name, string Region, string Label);

/// <summary>A pinned, local directory. Names are never resolved through a third-party API.</summary>
public static class CityCatalog
{
    private const string ResourceName = "BikeHub.Dal.ReferenceData.Cities.v1.json";
    private const int MaximumResults = 12;
    public const int MaximumQueryLength = 100;
    private sealed record SourceCity(Guid Id, string Name, string Region, string Label, string[] Aliases, int Population);
    private sealed record Entry(CityOption Option, string[] Keys, int Population);
    private static readonly Entry[] Entries = Load();
    private static readonly Dictionary<string, CityOption[]> Exact = Entries
        .SelectMany(entry => entry.Keys.Select(key => (key, entry.Option)))
        .GroupBy(item => item.key, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Select(item => item.Option).Distinct().ToArray(), StringComparer.Ordinal);

    private static Entry[] Load()
    {
        using var stream = typeof(CityCatalog).Assembly.GetManifestResourceStream(ResourceName)!;
        var cities = JsonSerializer.Deserialize<SourceCity[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return cities.Select(city => new Entry(new(city.Id, city.Name, city.Region, city.Label),
                new[] { city.Name, city.Label, city.Name + " " + city.Region }.Concat(city.Aliases)
                    .Select(Key).Distinct(StringComparer.Ordinal).ToArray(), city.Population))
            .OrderByDescending(city => city.Population).ThenBy(city => city.Option.Label, StringComparer.Ordinal).ToArray();
    }

    public static string Key(string? value)
    {
        var text = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        if (text.StartsWith("город ", StringComparison.Ordinal))
        {
            text = text[6..];
        }
        else if (text.StartsWith("г.", StringComparison.Ordinal) || text.StartsWith("г ", StringComparison.Ordinal))
        {
            text = text[2..];
        }
        return string.Concat(text.Where(char.IsLetterOrDigit)).Replace('ё', 'е');
    }

    public static CityOption? Resolve(string? value) => value?.Length <= MaximumQueryLength && Exact.TryGetValue(Key(value), out var matches) && matches.Length == 1 ? matches[0] : null;

    public static IReadOnlyList<CityOption> Search(string? value)
    {
        if (value?.Length > MaximumQueryLength)
        {
            return [];
        }
        var key = Key(value);
        if (key.Length == 0)
        {
            return Entries.Take(MaximumResults).Select(entry => entry.Option).ToArray();
        }
        var ranked = Entries.Select(entry => (Entry: entry, Rank: entry.Keys.Min(candidate =>
            candidate == key ? 0 : candidate.StartsWith(key, StringComparison.Ordinal) ? 1 :
            candidate.Contains(key, StringComparison.Ordinal) ? 2 :
            key.Length >= 4 && EditDistance(key, candidate, key.Length >= 8 ? 2 : 1) ? 3 : 4)));
        return ranked.Where(item => item.Rank < 4).OrderBy(item => item.Rank).ThenByDescending(item => item.Entry.Population)
            .ThenBy(item => item.Entry.Option.Label, StringComparer.Ordinal).Take(MaximumResults).Select(item => item.Entry.Option).ToArray();
    }

    // Fuzzy matches are suggestions only. Writes require an unambiguous exact name or alias.
    private static bool EditDistance(string left, string right, int limit)
    {
        if (Math.Abs(left.Length - right.Length) > limit)
        {
            return false;
        }
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var next = new int[right.Length + 1]; next[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                next[j] = Math.Min(Math.Min(previous[j] + 1, next[j - 1] + 1), previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            }
            if (next.Min() > limit)
            {
                return false;
            }
            previous = next;
        }
        return previous[^1] <= limit;
    }

}
