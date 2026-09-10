using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MS.Microservice.Infrastructure.Telemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MS.Microservice.Infrastructure.Telemetry.Microsoft.Extensions.DependencyInjection;

public static partial class OpenTelemetryExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddMsOpenTelemetry(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            var section = configuration.GetSection(TelemetryResourceOptions.SectionName);
            var resourceOptions = section.Get<TelemetryResourceOptions>() ?? new TelemetryResourceOptions();
            Validate(resourceOptions);

            services.AddOptions<TelemetryResourceOptions>()
                .Bind(section)
                .Validate(option => !string.IsNullOrWhiteSpace(option.ServiceName), "OpenTelemetry:ServiceName is required.")
                .Validate(option => !string.IsNullOrWhiteSpace(option.ServiceVersion), "OpenTelemetry:ServiceVersion is required.")
                .Validate(option => !string.IsNullOrWhiteSpace(option.EnvironmentName), "OpenTelemetry:EnvironmentName is required.")
                .Validate(option => !string.IsNullOrWhiteSpace(option.ActivitySourceName), "OpenTelemetry:ActivitySourceName is required.")
                .ValidateOnStart();

            services.AddOpenTelemetry()
                .ConfigureResource(resourceBuilder =>
                {
                    resourceBuilder
                        .AddService(
                            resourceOptions.ServiceName,
                            resourceOptions.ServiceNamespace,
                            resourceOptions.ServiceVersion,
                            autoGenerateServiceInstanceId: string.IsNullOrWhiteSpace(resourceOptions.ServiceInstanceId),
                            serviceInstanceId: resourceOptions.ServiceInstanceId)
                        .AddAttributes([
                            new KeyValuePair<string, object>(
                                "deployment.environment.name",
                                resourceOptions.EnvironmentName)
                        ]);
                })
                .WithTracing(configurationBuilder =>
                {
                    configurationBuilder.AddSource(resourceOptions.ActivitySourceName, "MS.Microservice.Messaging", "Wolverine", "MS.Microservice.AI")
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation();
                    if (resourceOptions.ConsoleExporterEnabled) configurationBuilder.AddConsoleExporter();
                    if (resourceOptions.OtlpExporterEnabled) configurationBuilder.AddOtlpExporter();
                })
                .WithMetrics(configurationBuilder =>
                {
                    configurationBuilder.AddMeter(PlatformMetrics.MeterName, "MS.Microservice.Messaging", "Wolverine*")
                        .AddRuntimeInstrumentation()
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation();
                    if (resourceOptions.ConsoleExporterEnabled) configurationBuilder.AddConsoleExporter();
                    if (resourceOptions.OtlpExporterEnabled) configurationBuilder.AddOtlpExporter();
                });
            services.TryAddSingleton<PlatformMetrics>();
            services.TryAddSingleton(new PlatformTracing(resourceOptions.ActivitySourceName));
            return services;
        }
    }

    private static void Validate(TelemetryResourceOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ServiceName))
        {
            failures.Add("OpenTelemetry:ServiceName is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ServiceVersion))
        {
            failures.Add("OpenTelemetry:ServiceVersion is required.");
        }

        if (string.IsNullOrWhiteSpace(options.EnvironmentName))
        {
            failures.Add("OpenTelemetry:EnvironmentName is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ActivitySourceName))
        {
            failures.Add("OpenTelemetry:ActivitySourceName is required.");
        }

        if (failures.Count != 0)
        {
            throw new OptionsValidationException(
                TelemetryResourceOptions.SectionName,
                typeof(TelemetryResourceOptions),
                failures);
        }
    }
}
