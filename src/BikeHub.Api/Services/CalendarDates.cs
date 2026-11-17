namespace BikeHub.Api.Services;

internal static class CalendarDates
{
    private static readonly DateOnly Earliest = new(1900, 1, 1);
    internal static void ValidatePast(DateOnly date, string? timeZone, DateTimeOffset now, string title)
    {
        TimeZoneInfo zone;
        try { zone = timeZone is null ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(timeZone); }
        catch (TimeZoneNotFoundException) { throw new CommunityProblem(400, title); }
        catch (InvalidTimeZoneException) { throw new CommunityProblem(400, title); }
        if (date > DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime) || date < Earliest)
        {
            throw new CommunityProblem(400, title);
        }
    }
}
