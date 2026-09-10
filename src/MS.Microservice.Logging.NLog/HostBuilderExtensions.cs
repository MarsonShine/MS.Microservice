using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace MS.Microservice.Logging.NLog;

public static class HostBuilderExtensions
{
    public static IHostApplicationBuilder ConfigureMsNLog(
        this IHostApplicationBuilder builder,
        Action<MsNLogOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = BuildOptions(configure);
        NLogConfigurationFactory.Configure(options);

        if (options.ClearProviders)
        {
            builder.Logging.ClearProviders();
        }

        if (options.MinimumLevel.HasValue)
        {
            builder.Logging.SetMinimumLevel(options.MinimumLevel.Value);
        }

        builder.Logging.AddNLog();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, NLogHostedService>());

        return builder;
    }

    public static IHostBuilder ConfigureMsNLog(
        this IHostBuilder builder,
        Action<MsNLogOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = BuildOptions(configure);
        NLogConfigurationFactory.Configure(options);

        builder.ConfigureLogging(logging =>
        {
            if (options.ClearProviders)
            {
                logging.ClearProviders();
            }

            if (options.MinimumLevel.HasValue)
            {
                logging.SetMinimumLevel(options.MinimumLevel.Value);
            }

            logging.AddNLog();
        });

        builder.ConfigureServices(services =>
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, NLogHostedService>());
        });

        return builder;
    }

    private static MsNLogOptions BuildOptions(Action<MsNLogOptions>? configure)
    {
        var options = new MsNLogOptions();
        configure?.Invoke(options);
        return options;
    }
}