using System.Net;
using System.Text.Json;
using BikeHub.Application.Community;

namespace BikeHub.Api.Tests;

public sealed class BikeCategoryTests
{
    [Theory]
    [InlineData(BikeCategory.Road, "road")]
    [InlineData(BikeCategory.Mtb, "mtb")]
    [InlineData(BikeCategory.Gravel, "gravel")]
    [InlineData(BikeCategory.City, "city")]
    public void Categories_round_trip_as_existing_json_strings(BikeCategory category, string value)
    {
        var json = JsonSerializer.Serialize(category);
        Assert.Equal(JsonSerializer.Serialize(value), json);
        Assert.Equal(category, JsonSerializer.Deserialize<BikeCategory>(json));
    }

    [Theory]
    [InlineData("road")]
    [InlineData("mtb")]
    [InlineData("gravel")]
    [InlineData("city")]
    public async Task Existing_category_strings_work_for_filtering_and_creation(string category)
    {
        await using var app = new AuthFactory();
        using var browser = app.Browser();
        foreach (var path in new[] { "/api/bikes?category=", "/api/feed?category=" })
        {
            using var response = await browser.GetAsync(path + category);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
            Assert.NotEmpty(items);
            Assert.All(items, item => Assert.Equal(category,
                (path.StartsWith("/api/feed") ? item.GetProperty("bike") : item).GetProperty("category").GetString()));
        }

        var (_, login) = await AuthTestClient.Login(browser, "category-test@example.test");
        using (login)
        using (var created = await AuthTestClient.Post(browser, "/api/bikes", new
        {
            name = "Category test bike", brand = "Canyon", category, year = 2024, mileage = 0, description = ""
        }))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            Assert.Equal(category, json.RootElement.GetProperty("category").GetString());
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"invalid\"")]
    [InlineData("1")]
    [InlineData("\"1\"")]
    [InlineData("999")]
    public async Task Invalid_json_categories_are_rejected(string categoryJson)
    {
        await using var app = new AuthFactory();
        using var browser = app.Browser();
        var (_, login) = await AuthTestClient.Login(browser, "category-test@example.test");
        using (login)
        using (var category = JsonDocument.Parse(categoryJson))
        using (var response = await AuthTestClient.Post(browser, "/api/bikes", new
        {
            name = "Invalid category bike", brand = "Canyon", category = category.RootElement,
            year = 2024, mileage = 0, description = ""
        }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Missing_category_does_not_default_to_a_valid_bike_type()
    {
        await using var app = new AuthFactory();
        using var browser = app.Browser();
        var (_, login) = await AuthTestClient.Login(browser, "category-test@example.test");
        using (login)
        using (var response = await AuthTestClient.Post(browser, "/api/bikes", new
        {
            name = "Missing category bike", brand = "Canyon", year = 2024, mileage = 0, description = ""
        }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("/api/bikes?category=invalid")]
    [InlineData("/api/feed?category=999")]
    public async Task Invalid_category_filters_return_validation_errors(string path)
    {
        await using var app = new AuthFactory();
        using var browser = app.Browser();
        using var response = await browser.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
