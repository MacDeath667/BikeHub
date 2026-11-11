using System.Text.Json;
using System.Text.Json.Serialization;

namespace BikeHub.Application.Community;

public sealed record GeoPoint([property: JsonRequired] double Lat, [property: JsonRequired] double Lng);
public sealed record RouteMarker([property: JsonRequired] double Lat, [property: JsonRequired] double Lng, string Label);
public sealed record MapRoute(string Name, GeoPoint[][] Segments, RouteMarker[] Markers);

public static class RouteJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string? Write(MapRoute? route) => route is null ? null : JsonSerializer.Serialize(route, Options);
    public static MapRoute? Read(string? json) => json is null ? null : JsonSerializer.Deserialize<MapRoute>(json, Options);
}
