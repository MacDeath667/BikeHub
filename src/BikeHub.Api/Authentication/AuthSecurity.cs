using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace BikeHub.Api.Authentication;

public sealed class OtpSecurity(IOptions<AuthOptions> options)
{
    private const string EmailCodePurpose = "email-code";
    private const string CodeFormat = "D6";
    private const int OtpKeyByteLength = 32;
    private const int CodeUpperBound = 1_000_000;
    private const int MaximumEmailLocalPartLength = 64;

    private readonly byte[] key = string.IsNullOrEmpty(options.Value.OtpSecret)
        ? RandomNumberGenerator.GetBytes(OtpKeyByteLength)
        : Encoding.UTF8.GetBytes(options.Value.OtpSecret);

    public string Hash(string purpose, string value) =>
        Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(purpose + ":" + value)));

    public string HashCode(Guid id, string code) => Hash(EmailCodePurpose, $"{id:N}:{code}");
    public static string NewCode() => RandomNumberGenerator.GetInt32(CodeUpperBound).ToString(CodeFormat, System.Globalization.CultureInfo.InvariantCulture);
    public static string NewBinding() => Convert.ToHexString(RandomNumberGenerator.GetBytes(Constants.BrowserBindingByteLength));
    public static bool EqualsHash(string expected, string actual) => CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual));

    public static string? NormalizeEmail(string input)
    {
        var value = input.Trim();
        if (value.Length > Constants.MaximumEmailLength || value.Contains('\r') || value.Contains('\n') ||
            !MailAddress.TryCreate(value, out var parsed) || parsed.Address != value ||
            parsed.User.Length > MaximumEmailLocalPartLength || !parsed.Host.Contains('.'))
        {
            return null;
        }

        return value.ToLowerInvariant();
    }

    public static bool IsLocalDevelopment(HttpContext context, IHostEnvironment environment) =>
        environment.IsDevelopment() && context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
}

public static class AuthTelemetry
{
    private const string EventCounterName = "bikehub.auth.events";
    private const string OutcomeTag = "outcome";

    private static readonly Meter Meter = new(Constants.AuthenticationName);
    private static readonly Counter<long> Events = Meter.CreateCounter<long>(EventCounterName);
    public static void Record(string outcome) => Events.Add(1, new KeyValuePair<string, object?>(OutcomeTag, outcome));
}
