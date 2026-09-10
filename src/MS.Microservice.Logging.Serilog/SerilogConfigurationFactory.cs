using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace MS.Microservice.Logging.Serilog;

internal static class SerilogConfigurationFactory
{
    public static void Configure(
        IConfiguration configuration,
        IServiceProvider services,
        LoggerConfiguration loggerConfiguration,
        MsSerilogOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(loggerConfiguration);
        ArgumentNullException.ThrowIfNull(options);

        if (options.MinimumLevel == LogLevel.None)
        {
            loggerConfiguration.Filter.ByExcluding(static _ => true);
        }

        loggerConfiguration.MinimumLevel.Is(ToSerilogLevel(options.MinimumLevel));
        loggerConfiguration.Enrich.FromLogContext();
        loggerConfiguration.Enrich.With(new RequestLogContextEnricher());

        if (options.ReadFromConfiguration)
        {
            loggerConfiguration.ReadFrom.Configuration(configuration);
        }

        if (options.UseConsoleSink)
        {
            loggerConfiguration.WriteTo.Console(
                outputTemplate: "{Timestamp:O} [{Level:u3}] {Message:lj} requestId={requestId} platformId={platformId} userflag={userflag} elapsedMs={elapsedMs}{NewLine}{Exception}");
        }

        options.ConfigureLogger?.Invoke(services, loggerConfiguration);
    }

    private static LogEventLevel ToSerilogLevel(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        LogLevel.None => LogEventLevel.Fatal,
        _ => LogEventLevel.Information,
    };
}