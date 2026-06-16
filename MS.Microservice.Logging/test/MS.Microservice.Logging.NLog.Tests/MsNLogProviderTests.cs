using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MS.Microservice.Logging.Core;
using NLog.Config;
using NLog.Targets;
using Xunit;

using System.IO;

namespace MS.Microservice.Logging.NLog.Tests;

public sealed class MsNLogProviderTests : IDisposable
{
    [Fact]
    public void ConfigureMsNLog_ShouldCreateFallbackConfiguration_WhenConfigFileIsMissing()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.ConfigureMsNLog(options =>
        {
            options.ConfigurationFilePath = "missing.nlog.config";
            options.UseFallbackConfigurationWhenFileMissing = true;
        });

        using var host = builder.Build();

        global::NLog.LogManager.Configuration.Should().NotBeNull();
        global::NLog.LogManager.Configuration!.AllTargets.Should().ContainSingle(target => target.Name == "console");
    }

    [Fact]
    public void ConfigureMsNLog_ShouldLoadSampleConfig_WithAspNetRequestRenderers()
    {
        var sampleConfigPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "MS.Microservice.Logging.NLog", "nlog.sample.config"));

        var builder = Host.CreateApplicationBuilder();

        var act = () => builder.ConfigureMsNLog(options =>
        {
            options.ConfigurationFilePath = sampleConfigPath;
            options.UseFallbackConfigurationWhenFileMissing = false;
        });

        act.Should().NotThrow();
        global::NLog.LogManager.Configuration.Should().NotBeNull();
    }

    [Fact]
    public void ConfigureMsNLog_ShouldRenderAmbientRequestContextIntoLayoutRenderers()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.ConfigureMsNLog(options =>
        {
            options.ConfigurationFilePath = "missing.nlog.config";
            options.UseFallbackConfigurationWhenFileMissing = true;
        });

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILogger<MsNLogProviderTests>>();

        var memoryTarget = new MemoryTarget("memory")
        {
            Layout = "rid=${requestId}|pid=${platformId}|uflag=${userflag}|dur=${RequestDuration}|msg=${message}",
        };

        var configuration = new LoggingConfiguration();
        configuration.AddTarget(memoryTarget);
        configuration.LoggingRules.Add(new LoggingRule("*", global::NLog.LogLevel.Info, memoryTarget));
        global::NLog.LogManager.Configuration = configuration;
        global::NLog.LogManager.ReconfigExistingLoggers();

        using (RequestLogScope.Push(new RequestLogContext
        {
            RequestId = "req-001",
            PlatformId = "platform-a",
            UserFlag = "u-flag",
            ElapsedMilliseconds = 64,
        }))
        {
            logger.LogInformation("hello nlog");
        }

        global::NLog.LogManager.Flush();

        memoryTarget.Logs.Should().ContainSingle();
        memoryTarget.Logs[0].Should().Be("rid=req-001|pid=platform-a|uflag=u-flag|dur=64ms|msg=hello nlog");
    }

    [Fact]
    public void ConfigureMsNLog_ShouldDisableRules_WhenMinimumLevelIsNone()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.ConfigureMsNLog(options =>
        {
            options.ConfigurationFilePath = "missing.nlog.config";
            options.UseFallbackConfigurationWhenFileMissing = true;
            options.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.None;
        });

        using var host = builder.Build();
        var rule = global::NLog.LogManager.Configuration!.LoggingRules.Should().ContainSingle().Subject;

        rule.IsLoggingEnabledForLevel(global::NLog.LogLevel.Info).Should().BeFalse();
        rule.IsLoggingEnabledForLevel(global::NLog.LogLevel.Error).Should().BeFalse();
        rule.IsLoggingEnabledForLevel(global::NLog.LogLevel.Fatal).Should().BeFalse();
    }

    public void Dispose()
    {
        global::NLog.LogManager.Configuration = null;
        global::NLog.LogManager.Shutdown();
    }
}