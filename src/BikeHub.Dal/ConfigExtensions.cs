namespace BikeHub.Dal;

public static class ConfigExtensions
{
    private const string NullConfigurationMessage = "Was null";

    public static T Protect<T>(this T? obj) where T : class
    {
        return obj ?? throw new NullReferenceException(NullConfigurationMessage);
    }
}