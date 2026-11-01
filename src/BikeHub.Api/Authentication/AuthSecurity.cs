using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace BikeHub.Api.Authentication;

public sealed class OtpSecurity(IOptions<AuthOptions> options)
{
    private readonly byte[] key = string.IsNullOrEmpty(options.Value.OtpSecret)
        ? RandomNumberGenerator.GetBytes(32)
        : Encoding.UTF8.GetBytes(options.Value.OtpSecret);

    public string Hash(string purpose, string value) =>
        Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(purpose + ":" + value)));

    public string HashCode(Guid id, string code) => Hash("email-code", $"{id:N}:{code}");
    public static string NewCode() => RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    public static string NewBinding() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static bool EqualsHash(string expected, string actual) => CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual));

    public static string? NormalizeEmail(string input)
    {
        var value = input.Trim();
        if (value.Length > 254 || value.Contains('\r') || value.Contains('\n') ||
            !MailAddress.TryCreate(value, out var parsed) || parsed.Address != value ||
            parsed.User.Length > 64 || !parsed.Host.Contains('.'))
            return null;
        return value.ToLowerInvariant();
    }

    public static bool IsLocalDevelopment(HttpContext context, IHostEnvironment environment) =>
        environment.IsDevelopment() && context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
}

public static class AuthTelemetry
{
    public const string MeterName = "BikeHub.Authentication";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Events = Meter.CreateCounter<long>("bikehub.auth.events");
    public static void Record(string outcome) => Events.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
