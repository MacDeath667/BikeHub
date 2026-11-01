using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BikeHub.Mail.Transports;
using Microsoft.Extensions.Configuration;

namespace BikeHub.Tools;

public static class Program
{
    private const string Usage = """
        BikeHub operator tools:
          mail gmail --client-json PATH --sender EMAIL --target local|server
          mail smtp --target local|server
          mail check --target server
          observability --api-url URL --vm-address IP [--environment Development|Production] [--ca-certificate PATH]
        Server options: --vm-address IP --ssh-key PATH.
        Passwords and tokens are passed via stdin, never command arguments.
        """;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine(Usage);
                return 0;
            }
            var offset = args[0] == "mail" ? 2 : 1;
            if (args.Length < offset || (args.Length - offset) % 2 != 0)
            {
                throw new ArgumentException();
            }
            var options = Enumerable.Range(0, (args.Length - offset) / 2)
                .ToDictionary(i => args[offset + i * 2], i => args[offset + i * 2 + 1]);
            if (args[0] == "observability")
            {
                await ObservabilityCheck.RunAsync(options);
                return 0;
            }
            if (args[0] != "mail")
            {
                throw new ArgumentException();
            }
            var target = options.GetValueOrDefault("--target") ?? throw new ArgumentException();
            if (target is not ("local" or "server"))
            {
                throw new ArgumentException();
            }
            var root = FindRoot();
            Dictionary<string, string?> settings;
            if (args[1] == "check" && target == "server")
            {
                await SaveServerAsync(root, [], options, true);
                return 0;
            }
            if (target == "server")
            {
                // Verify reachability before starting an interactive OAuth grant.
                await RunAsync("ansible", ["all", "-i", options.GetValueOrDefault("--vm-address", "192.168.0.49") + ",",
                    "-u", "ansible", "--private-key", Expand(options.GetValueOrDefault("--ssh-key", "~/.ssh/truenas_admin")),
                    "-m", "ansible.builtin.ping"], null, root);
            }
            if (args[1] == "gmail")
            {
                settings = await GmailAuthorization.AuthorizeAsync(options["--client-json"], options["--sender"], root);
            }
            else if (args[1] == "smtp")
            {
                Console.Write("Адрес Gmail отправителя: ");
                var sender = Console.ReadLine()?.Trim().ToLowerInvariant() ?? string.Empty;
                Console.Write("Пароль приложения Google (ввод скрыт): ");
                var password = ReadSecret().Replace(" ", string.Empty);
                if (!System.Text.RegularExpressions.Regex.IsMatch(password, "^[A-Za-z]{16}$") ||
                    !System.Text.RegularExpressions.Regex.IsMatch(sender, @"^[^\s@]+@(gmail|googlemail)\.com$"))
                {
                    throw new ArgumentException();
                }
                settings = new()
                {
                    ["Mail:Provider"] = "Smtp", ["Smtp:Host"] = "smtp.gmail.com", ["Smtp:Port"] = "587",
                    ["Smtp:Security"] = "StartTls", ["Smtp:Username"] = sender, ["Smtp:Password"] = password,
                    ["Smtp:FromAddress"] = sender
                };
            }
            else
            {
                throw new ArgumentException();
            }
            if (target == "local")
            {
                var result = await TransportProbe.CheckAsync(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), default);
                if (!result.Ok)
                {
                    Console.Error.WriteLine($"Проверка не прошла: stage={result.Stage}; reason={result.Reason}; status={result.Status}. Настройки не изменены.");
                    return 1;
                }
                await RunAsync("dotnet", ["user-secrets", "set", "--project", Path.Combine(root, "src/BikeHub.Mail")],
                    JsonSerializer.Serialize(settings), root);
                Console.WriteLine("Настройки сохранены в user-secrets BikeHub.Mail. Письмо не отправлялось.");
            }
            else
            {
                await SaveServerAsync(root, settings, options, false);
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Операция прервана или истекло время ожидания. Секреты не выводятся.");
            return 1;
        }
        catch (HttpRequestException error)
        {
            Console.Error.WriteLine($"HTTP-проверка не прошла: {error.HttpRequestError}; status={(int?)error.StatusCode}. Секреты и исходный ответ скрыты.");
            return 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Операция не выполнена ({error.GetType().Name}). Проверьте параметры (--help), доступ и конфигурацию. Подробности скрыты, чтобы не вывести секреты.");
            return 1;
        }
    }

    private static async Task SaveServerAsync(string root, Dictionary<string, string?> settings,
        Dictionary<string, string> options, bool checkOnly)
    {
        var values = new { bikehub_mail_configuration = settings, bikehub_mail_check_only = checkOnly };
        await RunAsync("ansible-playbook", ["-i", options.GetValueOrDefault("--vm-address", "192.168.0.49") + ",", "-u", "ansible",
            "--private-key", Expand(options.GetValueOrDefault("--ssh-key", "~/.ssh/truenas_admin")),
            "infra/ansible/playbooks/configure-mail.yml", "-e", "@infra/ansible/inventory/group_vars/bikehub/main.yml",
            "-e", "bikehub_target_group=all", "-e", "@/dev/stdin"], JsonSerializer.Serialize(values), root);
        Console.WriteLine(checkOnly ? "Проверка транспорта на VM завершена. Письмо не отправлялось." :
            "Почтовые настройки проверены на VM и сохранены. Прежний OTP-ключ сохранён. Выполните обычный Ansible deploy.");
    }

    public static string Expand(string path) => Path.GetFullPath(path.StartsWith("~/", StringComparison.Ordinal)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]) : path);

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BikeHub.sln")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException();
    }

    private static string ReadSecret()
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException();
        }
        var secret = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return secret.ToString();
            }
            if (key.Key == ConsoleKey.Backspace && secret.Length > 0)
            {
                secret.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                secret.Append(key.KeyChar);
            }
        }
    }

    private static async Task RunAsync(string executable, string[] arguments, string? input, string root)
    {
        using var process = new Process { StartInfo = new(executable)
        {
            WorkingDirectory = root, UseShellExecute = false, RedirectStandardInput = input is not null,
            RedirectStandardOutput = true, RedirectStandardError = true
        } };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        process.StartInfo.Environment["ANSIBLE_LOCAL_TEMP"] = Path.Combine(Path.GetTempPath(), "bikehub-ansible-local");
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0)
        {
            // Neither stream is echoed: validation errors may repeat submitted credentials.
            throw new InvalidOperationException();
        }
    }
}
