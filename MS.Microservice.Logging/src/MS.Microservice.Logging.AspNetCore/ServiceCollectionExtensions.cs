using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MS.Microservice.Logging.AspNetCore;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMsRequestLogging(
        this IServiceCollection services,
        Action<AspNetCoreRequestLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<AspNetCoreRequestLoggingOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }

    public static IApplicationBuilder UseMsRequestLogging(this IApplicationBuilder applicationBuilder)
    {
        ArgumentNullException.ThrowIfNull(applicationBuilder);

        return applicationBuilder.UseMiddleware<MsRequestLoggingMiddleware>();
    }
}