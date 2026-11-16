using BikeHub.Tools;
using Npgsql;
using Files = BikeHub.Tools.Constants.PostgresBackup;

namespace BikeHub.Mail.Tests;

public sealed class PostgresBackupConfigurationTests
{
    [Theory]
    [InlineData("/dev/null")]
    [InlineData("/dev/null/")]
    [InlineData("/")]
    [InlineData("relative-directory")]
    public async Task Sentinel_and_unsafe_destinations_are_rejected_before_credentials_are_read(string directory)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresBackupConfiguration.ExportAsync("MISSING_BACKUP_TEST_CONFIG", directory));
        Assert.Contains("directory", error.Message);
    }
    [Fact]
    public async Task Export_keeps_password_out_of_connection_arguments_and_creates_private_files()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var root = Path.Combine(Path.GetTempPath(), "bikehub-pg-config-" + Guid.NewGuid().ToString("N"));
        var variable = "BIKEHUB_BACKUP_TEST_" + Guid.NewGuid().ToString("N");
        const string secret = "fake'password:with\\slashes and spaces";
        Environment.SetEnvironmentVariable(variable, new NpgsqlConnectionStringBuilder
        {
            Host = "db.example.test", Port = 6543, Username = "backup'user", Database = "bike hub",
            Password = secret, SslMode = SslMode.VerifyFull, RootCertificate = "/certs/root cert.pem"
        }.ConnectionString);
        try
        {
            await PostgresBackupConfiguration.ExportAsync(variable, root);
            var connection = await File.ReadAllTextAsync(Path.Combine(root, Files.ConnectionFile));
            Assert.DoesNotContain(secret, connection); Assert.DoesNotContain("password=", connection);
            Assert.Contains("sslmode='verify-full'", connection); Assert.Contains("sslrootcert='/certs/root cert.pem'", connection);
            Assert.Contains("dbname='bike hub'", connection); Assert.Contains("user='backup\\'user'", connection);
            Assert.Equal("*:*:*:*:fake'password\\:with\\\\slashes and spaces\n", await File.ReadAllTextAsync(Path.Combine(root, Files.PasswordFile)));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(root));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(root, Files.PasswordFile)));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(root, Files.ConnectionFile)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresBackupConfiguration.ExportAsync(variable, root));
            Assert.Equal("*:*:*:*:fake'password\\:with\\\\slashes and spaces\n", await File.ReadAllTextAsync(Path.Combine(root, Files.PasswordFile)));
        }
        finally { Environment.SetEnvironmentVariable(variable, null); if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }
    [Fact]
    public async Task Bad_connection_strings_and_unrepresentable_passwords_do_not_leak_values_or_create_files()
    {
        var variable = "BIKEHUB_BACKUP_TEST_" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), "bikehub-pg-config-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var raw in new[] { "Unsupported-secret-marker=value", "Host=db;Username=backup;Password=\"secret-marker\nline\"", "Host=db;Username=backup;SSL Certificate=/private/cert.pem" })
            {
                Environment.SetEnvironmentVariable(variable, raw);
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresBackupConfiguration.ExportAsync(variable, root));
                Assert.DoesNotContain("secret-marker", error.Message); Assert.DoesNotContain("cert.pem", error.Message); Assert.False(Directory.Exists(root));
            }
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
    }
}
