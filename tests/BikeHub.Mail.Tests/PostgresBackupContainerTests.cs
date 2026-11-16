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
            await using (var inventory = typeof(PostgresBackupContainerTests).Assembly.GetManifestResourceStream("BikeHub.Backup.TableCounts.sql")!)
            await using (var file = File.Create(Path.Combine(root, "table-counts.sql")))
            {
                await inventory.CopyToAsync(file);
            }
            await using var postgres = new ContainerBuilder("postgres:18.6")
                .WithNetwork("none")
                .WithEnvironment("POSTGRES_USER", "bootstrap").WithEnvironment("POSTGRES_DB", "bootstrap").WithEnvironment("POSTGRES_PASSWORD", "fake-bootstrap-password")
                .WithBindMount(root, root, AccessMode.ReadOnly)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-h", "127.0.0.1", "-U", "bootstrap", "-d", "bootstrap"))
                .Build();
            await postgres.StartAsync();
            // initdb's extension bootstrap forbids quotes in its initial superuser name.
            // A separately created login can contain quotes and exercises actual libpq parsing.
            var role = "\"" + user.Replace("\"", "\"\"") + "\"";
            var createdRole = await postgres.ExecAsync(["psql", "-U", "bootstrap", "-d", "bootstrap", "-v", "ON_ERROR_STOP=1", "-c",
                "CREATE ROLE " + role + " LOGIN SUPERUSER PASSWORD '" + password.Replace("'", "''") + "'"]);
            Assert.Equal(0, createdRole.ExitCode);
            var createdDatabase = await postgres.ExecAsync(["psql", "-U", "bootstrap", "-d", "bootstrap", "-v", "ON_ERROR_STOP=1", "-c",
                "CREATE DATABASE \"" + database.Replace("\"", "\"\"") + "\" OWNER " + role]);
            Assert.Equal(0, createdDatabase.ExitCode);
            var connection = await File.ReadAllTextAsync(Path.Combine(root, Files.ConnectionFile));
            Assert.DoesNotContain(password, connection);
            var fixtures = await postgres.ExecAsync(["psql", "--no-psqlrc", "--dbname=" + connection, "--set=ON_ERROR_STOP=1", "--command",
                "CREATE SCHEMA mail; CREATE SCHEMA staging; CREATE TABLE public.api_fixture(id int); INSERT INTO public.api_fixture VALUES (1),(2); " +
                "CREATE TABLE mail.mail_fixture(id int); INSERT INTO mail.mail_fixture VALUES (1); CREATE TABLE staging.stage_fixture(id int); INSERT INTO staging.stage_fixture VALUES (1),(2),(3);"]);
            Assert.Equal(0, fixtures.ExitCode);
            var counts = await postgres.ExecAsync(["psql", "--no-psqlrc", "--tuples-only", "--no-align", "--dbname=" + connection, "--set=ON_ERROR_STOP=1", "--file=" + Path.Combine(root, "table-counts.sql")]);
            Assert.Equal(0, counts.ExitCode);
            Assert.Equal(new[] { "mail.mail_fixture|1", "public.api_fixture|2", "staging.stage_fixture|3" }, counts.Stdout.Trim().Split('\n').Select(line => line.Trim()));
            var dumped = await postgres.ExecAsync(["pg_dump", "--no-password", "--dbname=" + connection, "--format=custom", "--file=/tmp/backup.dump"]);
            Assert.True(dumped.ExitCode == 0, "Native pg_dump rejected the exported configuration; secret values are not logged.");
            var inspected = await postgres.ExecAsync(["pg_restore", "--list", "/tmp/backup.dump"]);
            Assert.Equal(0, inspected.ExitCode);
        }
        finally { Environment.SetEnvironmentVariable(variable, null); if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }
}
