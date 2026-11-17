namespace BikeHub.Api.Services;

internal static class CalendarDates
{
    private static readonly DateOnly Earliest = new(1900, 1, 1);
    internal static TimeZoneInfo Zone(string? timeZone, string title)
    {
        var id = timeZone ?? TimeZoneInfo.Utc.Id;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana)) { id = iana; }
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { throw new CommunityProblem(400, title); }
        catch (InvalidTimeZoneException) { throw new CommunityProblem(400, title); }
        if (!zone.HasIanaId && id != TimeZoneInfo.Utc.Id) { throw new CommunityProblem(400, title); }
        return zone;
    }
    internal static void ValidatePast(DateOnly date, string? timeZone, DateTimeOffset now, string title)
    {
        var zone = Zone(timeZone, title);
        if (date > DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime) || date < Earliest)
        {
            throw new CommunityProblem(400, title);
        }
    }
}
