using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MS.Microservice.Logging.Serilog;

public static class HostBuilderExtensions
{
    public static IHostApplicationBuilder ConfigureMsSerilog(
        this IHostApplicationBuilder builder,
        Action<MsSerilogOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = BuildOptions(configure);
        if (options.ClearProviders)
        {
            builder.Logging.ClearProviders();
        }

        builder.Logging.SetMinimumLevel(options.MinimumLevel);
        builder.Services.AddSerilog((services, loggerConfiguration) =>
        {
            SerilogConfigurationFactory.Configure(builder.Configuration, services, loggerConfiguration, options);
        });

        return builder;
    }

    public static IHostBuilder ConfigureMsSerilog(
        this IHostBuilder builder,
        Action<MsSerilogOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = BuildOptions(configure);
        return builder.UseSerilog((context, services, loggerConfiguration) =>
        {
            SerilogConfigurationFactory.Configure(context.Configuration, services, loggerConfiguration, options);
        });
    }

    private static MsSerilogOptions BuildOptions(Action<MsSerilogOptions>? configure)
    {
        var options = new MsSerilogOptions();
        configure?.Invoke(options);
        return options;
    }
}