using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Web.Infrastructure.LogUtils.Nlog.Configs;
using MS.Microservice.Web.Infrastructure.LogUtils.Nlog.LayoutRenderers;
using NLog;
using NLog.Web;
using System;
using System.IO;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog
{
    public class MSLoggerBuilder
    {
        private readonly IServiceCollection _services;
        public MSLoggerBuilder(IServiceCollection services)
        {
            _services = services;
        }

        public void WithNLogger(Action<LoggerConfig> config)
        {
            var loggerConfig = new LoggerConfig();
            config?.Invoke(loggerConfig);

            // Expose config to renderers
            NetAddressLayoutRenderer.Value = loggerConfig.NetAddress;
            LogLevelLayoutRenderer.Value = loggerConfig.LogLevel;

            // Register NLog.Web and custom layout renderers (Before loading config)
            LogManager.Setup().SetupExtensions(ext =>
            {
                // Register built-in NLog.Web AspNetCore extensions (aspnet-* layout renderers)
                ext.RegisterNLogWeb();

                // Register custom layout renderers
                ext.RegisterLayoutRenderer<RequestDurationLayoutRenderer>("RequestDuration");
                ext.RegisterLayoutRenderer<YearLayoutRenderer>("Year");
                ext.RegisterLayoutRenderer<MonthLayoutRenderer>("Month");
                ext.RegisterLayoutRenderer<HoursLayoutRenderer>("Hours");
                ext.RegisterLayoutRenderer<NetAddressLayoutRenderer>("NetAddress");
                ext.RegisterLayoutRenderer<LogLevelLayoutRenderer>("LogLevel");
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
        }
    }
}
