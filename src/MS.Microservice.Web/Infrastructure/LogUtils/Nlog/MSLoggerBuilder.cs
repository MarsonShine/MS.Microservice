using MS.Microservice.Web.Infrastructure.LogUtils.Nlog.Configs;
using MS.Microservice.Web.Infrastructure.LogUtils.Nlog.LayoutRenderers;
using NLog;
using NLog.Web;
using System;
using System.IO;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog
{
    public sealed class MSLoggerBuilder
    {
        public void WithNLogger(Action<LoggerConfig> config)
        {
            var loggerConfig = new LoggerConfig();
            config?.Invoke(loggerConfig);

            // Expose network address to renderer (used by commented-out Network target in nlog.config)
            NetAddressLayoutRenderer.Value = loggerConfig.NetAddress;

            // Register NLog.Web and custom layout renderers (Before loading config)
            LogManager.Setup().SetupExtensions(ext =>
            {
                ext.RegisterNLogWeb();
                ext.RegisterLayoutRenderer<RequestDurationLayoutRenderer>("RequestDuration");
                ext.RegisterLayoutRenderer<YearLayoutRenderer>("Year");
                ext.RegisterLayoutRenderer<MonthLayoutRenderer>("Month");
                ext.RegisterLayoutRenderer<HoursLayoutRenderer>("Hours");
                ext.RegisterLayoutRenderer<NetAddressLayoutRenderer>("NetAddress");
                ext.RegisterLayoutRenderer<RequestIdLayoutRenderer>("requestId");
                ext.RegisterLayoutRenderer<PlatformIdLayoutRenderer>("platformId");
                ext.RegisterLayoutRenderer<UserFlagLayoutRenderer>("userflag");
            });

            // Load from nlog.config if exists; otherwise setup minimal configuration
            var configPath = Path.Combine(AppContext.BaseDirectory, "nlog.config");
            if (File.Exists(configPath))
            {
                LogManager.Setup().LoadConfigurationFromFile(configPath);
            }
            else
            {
                LogManager.Setup().LoadConfiguration(builder =>
                {
                    var console = new NLog.Targets.ConsoleTarget("console") { DetectConsoleAvailable = true };
                    builder.Configuration.AddTarget(console);

                    var minLevel = NLog.LogLevel.FromString(loggerConfig.LogLevel ?? "Info");
                    builder.Configuration.AddRule(minLevel, NLog.LogLevel.Fatal, console);
                });
            }

            // 将 LoggerConfig.LogLevel 覆盖到所有已加载的 NLog 规则，确保配置生效
            ApplyLogLevelToRules(loggerConfig.LogLevel);
        }

        private static void ApplyLogLevelToRules(string? logLevel)
        {
            if (string.IsNullOrWhiteSpace(logLevel)) return;

            var minLevel = NLog.LogLevel.FromString(logLevel);
            if (minLevel == NLog.LogLevel.Off) return;

            // 已是 Trace（最宽松），无需收紧
            if (minLevel.Ordinal <= 0) return;

            var levelBelow = NLog.LogLevel.FromOrdinal(minLevel.Ordinal - 1);

            var configuration = LogManager.Configuration;
            if (configuration is null) return;

            foreach (var rule in configuration.LoggingRules)
            {
                // 如果规则允许比配置级别更低（更宽松）的日志，则收紧到配置级别
                if (rule.IsLoggingEnabledForLevel(levelBelow))
                {
                    rule.SetLoggingLevels(minLevel, NLog.LogLevel.Fatal);
                }
            }

            LogManager.ReconfigExistingLoggers();
        }
    }
}
