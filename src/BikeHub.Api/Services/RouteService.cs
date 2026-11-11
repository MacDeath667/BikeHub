using System.Globalization;
using System.Xml;
using BikeHub.Application.Community;

namespace BikeHub.Api.Services;

public static class RouteService
{
    public const long MaximumFileBytes = 5 * 1024 * 1024;
    public const int MaximumPoints = 30000;
    private const int MaximumMarkers = 100;
    private const int MaximumSegments = 100;
    private const string InvalidRoute = "Укажите маршрут до 30 000 точек, 100 отрезков и 100 меток с корректными координатами.";
    private const string InvalidGpx = "Выберите корректный GPX-файл до 5 МБ с треком, маршрутом или путевыми точками.";
    private const string ImportedName = "Маршрут из GPX";
    private static readonly string[] GpxNamespaces = ["", "http://www.topografix.com/GPX/1/0", "http://www.topografix.com/GPX/1/1"];
    private static bool Coordinate(double lat, double lng) => double.IsFinite(lat) && double.IsFinite(lng) && lat is >= -90 and <= 90 && lng is >= -180 and <= 180;

    public static string? Save(MapRoute? route)
    {
        if (route is null)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(route.Name) || route.Name.Length > 120 || route.Segments is null || route.Markers is null ||
            route.Segments.Length > MaximumSegments || route.Markers.Length > MaximumMarkers ||
            route.Segments.Any(s => s is null || s.Length < 2) ||
            route.Segments.Sum(s => (long)s.Length) + route.Markers.Length is < 1 or > MaximumPoints ||
            route.Segments.SelectMany(s => s).Any(p => p is null || !Coordinate(p.Lat, p.Lng)) ||
            route.Markers.Any(p => p is null || !Coordinate(p.Lat, p.Lng) || p.Label is null || p.Label.Length > 120))
        {
            throw new CommunityProblem(400, InvalidRoute);
        }
        return RouteJson.Write(route with { Name = route.Name.Trim() });
    }

    // Preserve segment endpoints and gaps while keeping feed-cover responses bounded.
    public static MapRoute Preview(MapRoute route)
    {
        const int maximumPoints = 2000;
        var total = route.Segments.Sum(segment => (long)segment.Length);
        var remaining = Math.Max(0, maximumPoints - route.Markers.Length - route.Segments.Length * 2);
        return route with
        {
            Segments = route.Segments.Select(segment =>
            {
                var count = Math.Min(segment.Length, 2 + (int)(segment.Length * (long)remaining / Math.Max(1, total)));
                return count >= segment.Length ? segment : Enumerable.Range(0, count)
                    .Select(index => segment[(int)Math.Round(index * (segment.Length - 1d) / (count - 1))]).ToArray();
            }).ToArray()
        };
    }

    public static MapRoute Import(Stream input)
    {
        try
        {
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = MaximumFileBytes, IgnoreComments = true, IgnoreWhitespace = true
            });
            var path = new List<string>();
            var segments = new List<GeoPoint[]>();
            var markers = new List<RouteMarker>();
            List<GeoPoint>? points = null;
            var count = 0;
            var root = false;
            while (reader.Read())
            {
                if (reader.Depth > 32)
                {
                    throw new XmlException();
                }
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if ((reader.LocalName is "trkseg" or "rte") && reader.Depth == (reader.LocalName == "rte" ? 1 : 2) && points is not null)
                    {
                        if (points.Count >= 2)
                        {
                            segments.Add(points.ToArray());
                        }
                        points = null;
                    }
                    if (path.Count > reader.Depth)
                    {
                        path.RemoveRange(reader.Depth, path.Count - reader.Depth);
                    }
                    continue;
                }
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }
                if (reader.Depth == 0)
                {
                    root = reader.LocalName == "gpx" && GpxNamespaces.Contains(reader.NamespaceURI);
                    if (!root)
                    {
                        throw new XmlException();
                    }
                }
                if (path.Count > reader.Depth)
                {
                    path.RemoveRange(reader.Depth, path.Count - reader.Depth);
                }
                var parent = path.LastOrDefault();
                if (GpxNamespaces.Contains(reader.NamespaceURI))
                {
                    if ((reader.LocalName == "trkseg" && reader.Depth == 2 && parent == "trk") || (reader.LocalName == "rte" && reader.Depth == 1))
                    {
                        points = [];
                    }
                    var point = (reader.LocalName == "trkpt" && reader.Depth == 3 && parent == "trkseg") ||
                        (reader.LocalName == "rtept" && reader.Depth == 2 && parent == "rte");
                    var waypoint = reader.LocalName == "wpt" && reader.Depth == 1;
                    if (point || waypoint)
                    {
                        if (!double.TryParse(reader.GetAttribute("lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                            !double.TryParse(reader.GetAttribute("lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lng) || !Coordinate(lat, lng) || ++count > MaximumPoints)
                        {
                            throw new XmlException();
                        }
                        if (waypoint)
                        {
                            markers.Add(new(lat, lng, $"Точка {markers.Count + 1}"));
                        }
                        else
                        {
                            points?.Add(new(lat, lng));
                        }
                    }
                }
                if (segments.Count > MaximumSegments || markers.Count > MaximumMarkers)
                {
                    throw new XmlException();
                }
                if (!reader.IsEmptyElement)
                {
                    path.Add(reader.LocalName);
                }
            }
            if (!root)
            {
                throw new XmlException();
            }
            var result = new MapRoute(ImportedName, segments.ToArray(), markers.ToArray());
            Save(result);
            return result;
        }
        catch (Exception error) when (error is XmlException or CommunityProblem)
        {
            throw new CommunityProblem(400, InvalidGpx);
        }
    }
}
