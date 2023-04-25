using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MS.Microservice.Infrastructure.Telemetry.Microsoft.Extensions.DependencyInjection
{
    public static class OpenTelemetryExtensions
    {
        public static IServiceCollection AddMsOpenTelemetry(this IServiceCollection services)
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
                    .AddJaegerExporter()
                    .AddAspNetCoreInstrumentation();
                });
            return services;
        }
    }
}
