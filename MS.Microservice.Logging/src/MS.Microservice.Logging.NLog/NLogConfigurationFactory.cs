using Microsoft.Extensions.Logging;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using MS.Microservice.Logging.NLog.LayoutRenderers;

namespace MS.Microservice.Logging.NLog;

internal static class NLogConfigurationFactory
{
    public static void Configure(MsNLogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        NLogProviderState.NetworkAddress = options.NetworkAddress;

        RegisterLayoutRenderers();

        var resolvedConfigPath = ResolveConfigPath(options.ConfigurationFilePath);
        if (resolvedConfigPath is not null)
        {
            global::NLog.LogManager.Configuration = new XmlLoggingConfiguration(resolvedConfigPath);
        }
        else if (options.UseFallbackConfigurationWhenFileMissing)
        {
            global::NLog.LogManager.Configuration = BuildFallbackConfiguration();
        }

        ApplyMinimumLevel(options.MinimumLevel);
    }

    private static void RegisterLayoutRenderers()
    {
        global::NLog.SetupBuilderExtensions.SetupExtensions(global::NLog.LogManager.Setup(), extensions =>
        {
            global::NLog.SetupExtensionsBuilderExtensions.RegisterLayoutRenderer(extensions, "RequestDuration", typeof(RequestDurationLayoutRenderer));
            global::NLog.SetupExtensionsBuilderExtensions.RegisterLayoutRenderer(extensions, "year", typeof(YearLayoutRenderer));
            global::NLog.SetupExtensionsBuilderExtensions.RegisterLayoutRenderer(extensions, "month", typeof(MonthLayoutRenderer));
            global::NLog.SetupExtensionsBuilderExtensions.RegisterLayoutRenderer(extensions, "hours", typeof(HoursLayoutRenderer));
            global::NLog.SetupExtensionsBuilderExtensions.RegisterLayoutRenderer(extensions, "NetAddress", typeof(NetAddressLayoutRenderer));
            global::NLog.SetupExtensionsBuilderExtensions.RegisterLayoutRenderer(extensions, "requestId", typeof(RequestIdLayoutRenderer));
            global::NLog.SetupExtensionsBuilderExtensions.RegisterLayoutRenderer(extensions, "platformId", typeof(PlatformIdLayoutRenderer));
            global::NLog.SetupExtensionsBuilderExtensions.RegisterLayoutRenderer(extensions, "userflag", typeof(UserFlagLayoutRenderer));
        });
    }

    private static string? ResolveConfigPath(string configurationFilePath)
    {
        if (string.IsNullOrWhiteSpace(configurationFilePath))
        {
            return null;
        }

        var path = Path.IsPathRooted(configurationFilePath)
            ? configurationFilePath
            : Path.Combine(AppContext.BaseDirectory, configurationFilePath);

        return File.Exists(path) ? path : null;
    }

    private static LoggingConfiguration BuildFallbackConfiguration()
    {
        var configuration = new LoggingConfiguration();
        var consoleTarget = new ConsoleTarget("console")
        {
            DetectConsoleAvailable = true,
            Layout = "${longdate} | ${uppercase:${level}} | ${logger} | rid=${requestId} pid=${platformId} uflag=${userflag} | dur=${RequestDuration} | msg=${message} ${onexception:inner=${exception:format=ToString}}",
        };

        configuration.AddTarget(consoleTarget);
        configuration.AddRule(global::NLog.LogLevel.Info, global::NLog.LogLevel.Fatal, consoleTarget);
        return configuration;
    }

    private static void ApplyMinimumLevel(Microsoft.Extensions.Logging.LogLevel? minimumLevel)
    {
        if (!minimumLevel.HasValue)
        {
            return;
        }

        var nlogLevel = ToNLogLevel(minimumLevel.Value);
        var configuration = global::NLog.LogManager.Configuration;
        if (configuration is null)
        {
            return;
        }

        if (nlogLevel == global::NLog.LogLevel.Off)
        {
            foreach (var rule in configuration.LoggingRules)
            {
                rule.DisableLoggingForLevels(global::NLog.LogLevel.Trace, global::NLog.LogLevel.Fatal);
            }

            global::NLog.LogManager.ReconfigExistingLoggers();
            return;
        }

        foreach (var rule in configuration.LoggingRules)
        {
            rule.SetLoggingLevels(nlogLevel, global::NLog.LogLevel.Fatal);
        }

        global::NLog.LogManager.ReconfigExistingLoggers();
    }

    private static global::NLog.LogLevel ToNLogLevel(Microsoft.Extensions.Logging.LogLevel logLevel) => logLevel switch
    {
        Microsoft.Extensions.Logging.LogLevel.Trace => global::NLog.LogLevel.Trace,
        Microsoft.Extensions.Logging.LogLevel.Debug => global::NLog.LogLevel.Debug,
        Microsoft.Extensions.Logging.LogLevel.Information => global::NLog.LogLevel.Info,
        Microsoft.Extensions.Logging.LogLevel.Warning => global::NLog.LogLevel.Warn,
        Microsoft.Extensions.Logging.LogLevel.Error => global::NLog.LogLevel.Error,
        Microsoft.Extensions.Logging.LogLevel.Critical => global::NLog.LogLevel.Fatal,
        Microsoft.Extensions.Logging.LogLevel.None => global::NLog.LogLevel.Off,
        _ => global::NLog.LogLevel.Info,
    };
}