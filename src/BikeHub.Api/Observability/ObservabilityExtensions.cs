using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BikeHub.Api.Observability;

public static class ObservabilityExtensions
{
    public static void AddBikeHubObservability(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration.GetSection("Observability");
        if (!configuration.GetValue<bool>("Enabled"))
            return;

        var endpointValue = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                            ?? configuration["Endpoint"];
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Observability:Endpoint must be an absolute HTTP(S) OTLP endpoint.");

        builder.Services.Configure<OpenTelemetryLoggerOptions>(options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.ParseStateValues = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: builder.Configuration["OTEL_SERVICE_NAME"] ?? "bikehub-api",
                    serviceVersion: typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString())
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment.name"] = builder.Environment.EnvironmentName
                }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter("System.Runtime")
                .AddView("http.server.request.duration", new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10]
                }))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
                    options.Filter = context => context.Request.Path != "/api/health";
                })
                .AddHttpClientInstrumentation()
                .AddSource("Npgsql"))
            .UseOtlpExporter(OtlpExportProtocol.HttpProtobuf, endpoint);
    }
}
