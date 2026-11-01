using System.Text.Json;
using System.Text.Json.Serialization;

namespace BikeHub.Application.Community;

[JsonConverter(typeof(BikeCategoryJsonConverter))]
public enum BikeCategory
{
    Road = 1,
    Mtb = 2,
    Gravel = 3,
    City = 4
}

public sealed class BikeCategoryJsonConverter : JsonStringEnumConverter<BikeCategory>
{
    public BikeCategoryJsonConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    {
    }
}
