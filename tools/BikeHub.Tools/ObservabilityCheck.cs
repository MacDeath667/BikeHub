using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace BikeHub.Tools;

public static class ObservabilityCheck
{
    private const string ServiceName = "bikehub-api";
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    public static async Task RunAsync(Dictionary<string, string> options)
    {
        var address = IPAddress.Parse(options.GetValueOrDefault("--vm-address", "192.168.0.49"));
        var api = new Uri(options.GetValueOrDefault("--api-url", "http://127.0.0.1:5179"));
        var environment = options.GetValueOrDefault("--environment", "Development");
        using var handler = new HttpClientHandler { UseProxy = false };
        if (options.TryGetValue("--ca-certificate", out var filename))
        {
            var certificate = X509Certificate2.CreateFromPem(File.ReadAllText(Program.Expand(filename)));
            handler.ServerCertificateCustomValidationCallback = (_, server, _, errors) =>
            {
                if (server is null || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0)
                {
                    return false;
                }
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(certificate);
                chain.ChainPolicy.ApplicationPolicy.Add(new Oid(ServerAuthenticationOid));
                return chain.Build(server);
            };
        }
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var traceId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(api, "/api/feed?pageSize=1"));
        request.Headers.Add("traceparent", $"00-{traceId}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}-01");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        Console.WriteLine($"API: OK; trace_id={traceId}");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var trace = JsonDocument.Parse(await client.GetStringAsync($"http://{address}:32000/api/traces/{traceId}"));
                var attributes = Walk(trace.RootElement).Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("key", out _)).ToList();
                var instance = attributes.First(x => x.GetProperty("key").GetString() == "service.instance.id").GetProperty("value").GetProperty("stringValue").GetString();
                var actualEnvironment = attributes.First(x => x.GetProperty("key").GetString() == "deployment.environment.name").GetProperty("value").GetProperty("stringValue").GetString();
                if (actualEnvironment != environment)
                {
                    throw new InvalidOperationException();
                }
                var traceIds = Walk(trace.RootElement).Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("traceId", out _))
                    .Select(x => x.GetProperty("traceId").GetString()).ToList();
                if (!traceIds.Contains(traceId) && !traceIds.Contains(Convert.ToBase64String(Convert.FromHexString(traceId))))
                {
                    throw new InvalidOperationException();
                }
                var query = Uri.EscapeDataString($"{{service_name=\"{ServiceName}\"}} | trace_id=\"{traceId}\"");
                using var logs = JsonDocument.Parse(await client.GetStringAsync($"http://{address}:31000/loki/api/v1/query_range?query={query}&limit=100"));
                var matchingLogs = logs.RootElement.GetProperty("data").GetProperty("result").EnumerateArray().Any(row =>
                    row.GetProperty("stream").TryGetProperty("service_instance_id", out var id) && id.GetString() == instance &&
                    row.GetProperty("stream").TryGetProperty("scope_name", out var scope) && scope.GetString() == "BikeHub.Api.Middleware.StatisticsMiddleware");
                query = Uri.EscapeDataString($"sum(http_server_request_duration_seconds_count{{service_name=\"{ServiceName}\",service_instance_id={JsonSerializer.Serialize(instance)}}})");
                using var metrics = JsonDocument.Parse(await client.GetStringAsync($"http://{address}:30900/api/v1/query?query={query}"));
                var metricRows = metrics.RootElement.GetProperty("data").GetProperty("result").EnumerateArray().ToList();
                if (matchingLogs && metricRows.Count > 0 && double.Parse(metricRows[0].GetProperty("value")[1].GetString()!, System.Globalization.CultureInfo.InvariantCulture) >= 1)
                {
                    Console.WriteLine("OK: Tempo trace, Loki log and Prometheus metric belong to the same API instance.");
                    return;
                }
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) { }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        throw new TimeoutException();
    }

    private static IEnumerable<JsonElement> Walk(JsonElement element)
    {
        yield return element;
        var children = element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().Select(x => x.Value),
            JsonValueKind.Array => element.EnumerateArray(),
            _ => Enumerable.Empty<JsonElement>()
        };
        foreach (var child in children)
        {
            foreach (var nested in Walk(child))
            {
                yield return nested;
            }
        }
    }
}
