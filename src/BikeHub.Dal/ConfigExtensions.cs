namespace BikeHub.Dal;

public static class ConfigExtensions
{
    public static T Protect<T>(this T? obj) where T : class
    {
        return obj ?? throw new NullReferenceException("Was null");
    }
}