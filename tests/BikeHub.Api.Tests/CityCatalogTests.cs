using System.Net;
using System.Net.Http.Json;
using BikeHub.Api.Services;
using BikeHub.Dal.ReferenceData;

namespace BikeHub.Api.Tests;

public sealed class CityCatalogTests
{
    [Theory]
    [InlineData("спб")]
    [InlineData("ПиТеР")]
    [InlineData(" Санкт Петербург ")]
    [InlineData("САНКТ‑ПЕТЕРБУРГ")]
    [InlineData("г. Санкт-Петербург")]
    [InlineData("город Санкт-Петербург")]
    public void City_aliases_resolve_to_one_canonical_label_and_fias_id(string input)
    {
        var city = CityCatalog.Resolve(input);
        Assert.NotNull(city);
        Assert.Equal("Санкт-Петербург", city.Label);
        Assert.Equal(Guid.Parse("c2deb16a-0330-4f05-821f-1d09c93331e6"), city.Id);
        Assert.Equal(city.Label, CitySelection.Normalize(input));
        Assert.Equal(city.Label, CitySelection.Filter(input));
    }

    [Fact]
    public void Ambiguous_names_require_a_region_and_fuzzy_suggestions_never_silently_choose_a_city()
    {
        Assert.Null(CityCatalog.Resolve("Киров"));
        var matches = CityCatalog.Search("Киров").Where(city => city.Name == "Киров").ToArray();
        Assert.Equal(2, matches.Length);
        Assert.Equal(2, matches.Select(city => city.Label).Distinct().Count());
        Assert.All(matches, city => Assert.Equal(city, CityCatalog.Resolve(city.Label)));
        Assert.Throws<CommunityProblem>(() => CitySelection.Normalize("Киров"));
        Assert.Null(CityCatalog.Resolve("Санкт-Петебург"));
        Assert.Contains(CityCatalog.Search("Санкт-Петебург"), city => city.Name == "Санкт-Петербург");
        Assert.Throws<CommunityProblem>(() => CitySelection.Normalize("Санкт-Петебург"));
        Assert.Equal(CityCatalog.Resolve("Орёл"), CityCatalog.Resolve("орел"));
        Assert.Equal("", CitySelection.Normalize(" ", optional: true));
        Assert.Equal("Неизвестный старый город", CitySelection.Normalize("Неизвестный старый город", existing: "Неизвестный старый город"));
        Assert.Throws<CommunityProblem>(() => CitySelection.Normalize("Другой неизвестный город", existing: "Неизвестный старый город"));
        Assert.InRange(CityCatalog.Search("").Count, 1, 12);
        Assert.Empty(CityCatalog.Search(new string('a', 101)));
    }

    [Fact]
    public async Task Public_city_directory_works_without_database_or_authentication_and_bounds_queries()
    {
        await using var app = new AuthFactory(); using var guest = app.Browser();
        using var response = await guest.GetAsync("/api/cities?q=" + Uri.EscapeDataString("ПИТЕР"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.CacheControl?.MaxAge);
        var city = Assert.Single((await response.Content.ReadFromJsonAsync<CityOption[]>())!, city => city.Name == "Санкт-Петербург");
        Assert.Equal("Санкт-Петербург", city.Label);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync("/api/cities?q=" + new string('a', 101))).StatusCode);
    }
}
