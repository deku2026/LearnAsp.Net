using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Campus.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    public static TBuilder AddCampusServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        var serviceName = builder.Environment.ApplicationName;
        var serviceVersion = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+', 2)[0] ?? "1.0.0";
        var samplingRatio = Math.Clamp(
            builder.Configuration.GetValue("Observability:SamplingRatio", 1d),
            0d,
            1d);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.ParseStateValues = true;
            if (HasOtlpEndpoint(builder.Configuration))
            {
                logging.AddOtlpExporter();
            }
        });

        var hasOtlpEndpoint = HasOtlpEndpoint(builder.Configuration);
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: serviceVersion)
                .AddAttributes(
                [
                    new("deployment.environment.name", builder.Environment.EnvironmentName),
                    new("service.namespace", "campus-learning"),
                ]))
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(CampusTelemetry.MeterName);
                if (hasOtlpEndpoint)
                {
                    metrics.AddOtlpExporter();
                }
            })
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new ParentBasedSampler(
                    new TraceIdRatioBasedSampler(samplingRatio)))
                    .AddSource(CampusTelemetry.ActivitySourceName)
                    .AddSource("Npgsql")
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.FilterHttpRequestMessage = request =>
                            request.RequestUri is not null &&
                            !request.RequestUri.AbsolutePath.StartsWith(
                                "/health",
                                StringComparison.OrdinalIgnoreCase);
                    });
                if (hasOtlpEndpoint)
                {
                    tracing.AddOtlpExporter();
                }
            });

        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;
        return builder;
    }

    public static WebApplication MapCampusDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => !check.Tags.Contains("live"),
        });
        return app;
    }

    private static bool HasOtlpEndpoint(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(
            configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
}
