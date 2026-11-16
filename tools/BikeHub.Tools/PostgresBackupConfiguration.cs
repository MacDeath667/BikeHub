using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.Versioning;
using Npgsql;
using static BikeHub.Tools.Constants.PostgresBackup;

namespace BikeHub.Tools;

// Runs in an isolated operator container. Credentials never pass through the Ansible controller.
public static class PostgresBackupConfiguration
{
    private const int MaximumConnectionStringLength = 16384;
    private const string EnvironmentVariablePattern = "^[A-Za-z_][A-Za-z0-9_]{0,127}$";
    private const string InvalidDestination = "Choose a new absolute private directory for PostgreSQL backup configuration.";
    private const string InvalidSettings = "PostgreSQL backup settings are missing or unsupported. Credential values are not logged.";
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private static readonly UTF8Encoding Utf8 = new(false);

    public static async Task ExportAsync(string environmentVariable, string directory)
    {
        ValidateDirectory(directory);
        if (!Regex.IsMatch(environmentVariable, EnvironmentVariablePattern, RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(InvalidSettings);
        }
        await ExportValueAsync(Environment.GetEnvironmentVariable(environmentVariable), directory);
    }

    public static async Task ExportFromStandardInputAsync(string directory)
    {
        ValidateDirectory(directory);
        await ExportValueAsync(await Console.In.ReadToEndAsync(), directory);
    }

    private static void ValidateDirectory(string directory)
    {
        if (!Path.IsPathFullyQualified(directory) || Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) == "/dev/null" || Path.GetFullPath(directory) == Path.GetPathRoot(directory) ||
            Directory.Exists(directory) || File.Exists(directory))
        {
            throw new InvalidOperationException(InvalidDestination);
        }
    }
    private static async Task ExportValueAsync(string? raw, string directory)
    {
        NpgsqlConnectionStringBuilder settings;
        try
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaximumConnectionStringLength)
            {
                throw new ArgumentException();
            }
            settings = new(raw);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(InvalidSettings);
        }
        var database = string.IsNullOrEmpty(settings.Database) ? settings.Username : settings.Database;
        if (string.IsNullOrEmpty(settings.Host) || string.IsNullOrEmpty(settings.Username) || string.IsNullOrEmpty(database) ||
            new[] { settings.Host, settings.Username, database, settings.Password }.Any(value => value?.IndexOfAny(['\r', '\n', '\0']) >= 0))
        {
            throw new InvalidOperationException(InvalidSettings);
        }
        var ssl = settings.SslMode switch
        {
            SslMode.Disable => "disable", SslMode.Allow => "allow", SslMode.Prefer => "prefer", SslMode.Require => "require",
            SslMode.VerifyCA => "verify-ca", SslMode.VerifyFull => "verify-full", _ => throw new InvalidOperationException(InvalidSettings)
        };
        var values = new Dictionary<string, string>
        {
            ["host"] = settings.Host, ["port"] = settings.Port.ToString(CultureInfo.InvariantCulture),
            ["user"] = settings.Username, ["dbname"] = database,
            ["passfile"] = Path.Combine(directory, PasswordFile), ["sslmode"] = ssl,
            ["connect_timeout"] = settings.Timeout.ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrEmpty(settings.RootCertificate))
        {
            values["sslrootcert"] = settings.RootCertificate;
        }
        if (!string.IsNullOrEmpty(settings.SslCertificate) || !string.IsNullOrEmpty(settings.SslKey))
        {
            // Client key mounts need a separate operator configuration; never silently drop TLS credentials.
            throw new InvalidOperationException(InvalidSettings);
        }
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(InvalidDestination);
        }
        Directory.CreateDirectory(directory, DirectoryMode);
        var connection = string.Join(" ", values.Select(pair => pair.Key + "='" + pair.Value.Replace("\\", "\\\\").Replace("'", "\\'") + "'"));
        var password = "*:*:*:*:" + (settings.Password ?? string.Empty).Replace("\\", "\\\\").Replace(":", "\\:") + "\n";
        await WritePrivateAsync(Path.Combine(directory, PasswordFile), password);
        await WritePrivateAsync(Path.Combine(directory, ConnectionFile), connection);
    }
    [UnsupportedOSPlatform("windows")]
    private static async Task WritePrivateAsync(string path, string value)
    {
        await using var stream = new FileStream(path, new FileStreamOptions { Mode = System.IO.FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = FileMode });
        await stream.WriteAsync(Utf8.GetBytes(value));
    }
}
