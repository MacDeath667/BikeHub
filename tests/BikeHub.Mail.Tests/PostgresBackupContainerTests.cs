using BikeHub.Tools;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using Npgsql;
using Files = BikeHub.Tools.Constants.PostgresBackup;

namespace BikeHub.Mail.Tests;

public sealed class PostgresBackupContainerTests
{
    [Fact, Trait("Category", "Container")]
    public async Task Native_pg_dump_accepts_exported_quotes_spaces_colons_and_backslashes_offline()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var root = Path.Combine(Path.GetTempPath(), "bikehub-pg-native-" + Guid.NewGuid().ToString("N") + " space's");
        var variable = "BIKEHUB_BACKUP_TEST_" + Guid.NewGuid().ToString("N");
        const string user = "backup'user", database = "backup database", password = "fake'secret:with\\slashes";
        Environment.SetEnvironmentVariable(variable, new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1", Port = 5432, Username = user, Database = database, Password = password, SslMode = SslMode.Disable
        }.ConnectionString);
        try
        {
            await PostgresBackupConfiguration.ExportAsync(variable, root);
            await using var postgres = new ContainerBuilder("postgres:18.6")
                .WithNetwork("none")
                .WithEnvironment("POSTGRES_USER", user).WithEnvironment("POSTGRES_DB", database).WithEnvironment("POSTGRES_PASSWORD", password)
                .WithBindMount(root, root, AccessMode.ReadOnly)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-h", "127.0.0.1", "-U", user, "-d", database))
                .Build();
            await postgres.StartAsync();
            var connection = await File.ReadAllTextAsync(Path.Combine(root, Files.ConnectionFile));
            Assert.DoesNotContain(password, connection);
            var dumped = await postgres.ExecAsync(["pg_dump", "--no-password", "--dbname=" + connection, "--format=custom", "--file=/tmp/backup.dump"]);
            Assert.True(dumped.ExitCode == 0, "Native pg_dump rejected the exported configuration; secret values are not logged.");
            var inspected = await postgres.ExecAsync(["pg_restore", "--list", "/tmp/backup.dump"]);
            Assert.Equal(0, inspected.ExitCode);
        }
        finally { Environment.SetEnvironmentVariable(variable, null); if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }
}
