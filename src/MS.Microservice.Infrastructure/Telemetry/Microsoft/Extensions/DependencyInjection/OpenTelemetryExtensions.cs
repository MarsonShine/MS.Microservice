using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MS.Microservice.Infrastructure.Telemetry.Microsoft.Extensions.DependencyInjection
{
    public static partial class OpenTelemetryExtensions
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddMsOpenTelemetry()
            {
                services.AddOpenTelemetry()
                    .ConfigureResource(resourceBuilder =>
                    {
                        // TODO: 配置化
                        resourceBuilder.AddService("Fz.OrderPlatform.Admin in OTel Service");
                    })
                    .WithTracing(cfg =>
                    {
                        // TODO: 配置化
                        cfg.AddSource("Fz.OrderPlatform.Admin in OTel Source")
                        .AddConsoleExporter()
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddOtlpExporter();
                    });
                return services;
            }
        }
    }
}
