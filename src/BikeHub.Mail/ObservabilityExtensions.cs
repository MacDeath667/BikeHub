using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BikeHub.Mail;

public static class ObservabilityExtensions
{
    private const string ConfigurationSection = "Observability";
    private const string EnabledKey = "Enabled";
    private const string EndpointEnvironmentVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string EndpointKey = "Endpoint";
    private const string InvalidEndpointMessage = "Observability:Endpoint must be an absolute HTTP(S) OTLP endpoint.";
    private const string HttpScheme = "http";
    private const string HttpsScheme = "https";
    private const string ServiceNameEnvironmentVariable = "OTEL_SERVICE_NAME";
    private const string DefaultServiceName = "bikehub-mail";
    private const string DeploymentEnvironmentAttribute = "deployment.environment.name";
    private const string RuntimeMeterName = "System.Runtime";
    private const string RequestDurationMetricName = "http.server.request.duration";
    private const string DatabaseActivitySourceName = "Npgsql";

    public static void AddBikeHubObservability(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration.GetSection(ConfigurationSection);
        if (!configuration.GetValue<bool>(EnabledKey))
        {
            return;
        }

        var endpointValue = builder.Configuration[EndpointEnvironmentVariable]
                            ?? configuration[EndpointKey];
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not (HttpScheme or HttpsScheme))
        {
            throw new InvalidOperationException(InvalidEndpointMessage);
        }

        builder.Services.Configure<OpenTelemetryLoggerOptions>(options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.ParseStateValues = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: builder.Configuration[ServiceNameEnvironmentVariable] ?? DefaultServiceName,
                    serviceVersion: typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString())
                .AddAttributes(new Dictionary<string, object>
                {
                    [DeploymentEnvironmentAttribute] = configuration["Environment"] ?? builder.Environment.EnvironmentName
                }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(RuntimeMeterName)
                .AddMeter(Constants.ApplicationName)
                .AddView(RequestDurationMetricName, new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10]
                }))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = false;
                    options.Filter = context => context.Request.Path != "/api/health";
                })
                .AddHttpClientInstrumentation()
                .AddSource(DatabaseActivitySourceName, Constants.ApplicationName))
            .UseOtlpExporter(OtlpExportProtocol.HttpProtobuf, endpoint);
    }
}
