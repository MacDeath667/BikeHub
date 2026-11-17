using BikeHub.Api.Services;
using BikeHub.Application.Community;

namespace BikeHub.Api.Tests;

public sealed class RouteDistanceTests
{
    [Fact]
    public void Distance_follows_segments_without_joining_gaps_or_detached_markers()
    {
        var route = new MapRoute("Segments", [[new(0, 0), new(0, 0.01)], [new(0, 10), new(0, 10.01)]], [new(80, 120, "Unconnected")]);
        Assert.InRange(RouteService.DistanceKm(route), 2.21m, 2.23m);
        Assert.Equal(0m, RouteService.DistanceKm(new MapRoute("Markers", [], route.Markers)));
        Assert.InRange(RouteService.DistanceKm(new MapRoute("Dateline", [[new(0, 179.9), new(0, -179.9)]], [])), 22.23m, 22.25m);
    }
}
