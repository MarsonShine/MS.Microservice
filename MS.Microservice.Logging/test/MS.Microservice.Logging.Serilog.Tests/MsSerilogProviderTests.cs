using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MS.Microservice.Logging.Core;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace MS.Microservice.Logging.Serilog.Tests;

public sealed class MsSerilogProviderTests
{
    [Fact]
    public void ConfigureMsSerilog_ShouldEnrichEventsWithAmbientRequestContext()
    {
        var sink = new CollectingSink();
        var builder = Host.CreateApplicationBuilder();
        builder.ConfigureMsSerilog(options =>
        {
            options.ReadFromConfiguration = false;
            options.UseConsoleSink = false;
            options.ConfigureLogger = (_, loggerConfiguration) =>
            {
                loggerConfiguration.WriteTo.Sink(sink);
            };
        });

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILogger<MsSerilogProviderTests>>();

        using (RequestLogScope.Push(new RequestLogContext
        {
            RequestId = "req-001",
            PlatformId = "platform-a",
            UserFlag = "u-flag",
            Method = "GET",
            Path = "/orders/42",
            StatusCode = 204,
            ElapsedMilliseconds = 88,
        }))
        {
            logger.LogInformation("hello serilog");
        }

        sink.Events.Should().ContainSingle();
        var logEvent = sink.Events[0];
        logEvent.Properties.Should().ContainKey(RequestLogDefaults.RequestIdPropertyName);
        logEvent.Properties.Should().ContainKey(RequestLogDefaults.PlatformIdPropertyName);
        logEvent.Properties.Should().ContainKey(RequestLogDefaults.UserFlagPropertyName);
        logEvent.Properties.Should().ContainKey(RequestLogDefaults.MethodPropertyName);
        logEvent.Properties.Should().ContainKey(RequestLogDefaults.PathPropertyName);
        logEvent.Properties.Should().ContainKey(RequestLogDefaults.StatusCodePropertyName);
        logEvent.Properties.Should().ContainKey(RequestLogDefaults.ElapsedMillisecondsPropertyName);
        logEvent.Properties[RequestLogDefaults.RequestIdPropertyName].ToString().Should().Be("\"req-001\"");
        logEvent.Properties[RequestLogDefaults.ElapsedMillisecondsPropertyName].ToString().Should().Be("88");
    }

    [Fact]
    public void ConfigureMsSerilog_ShouldOverrideOnlyFieldsPresentInTheCurrentContext()
    {
        var sink = new CollectingSink();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureMsSerilog(options =>
            {
                options.ReadFromConfiguration = false;
                options.UseConsoleSink = false;
                options.ConfigureLogger = (_, configuration) => configuration.WriteTo.Sink(sink);
            })
            .Build();
        var logger = host.Services.GetRequiredService<global::Serilog.ILogger>();
        var context = new RequestLogContext { RequestId = "ambient", PlatformId = " ", ElapsedMilliseconds = 0 };
        using (RequestLogScope.Push(context))
        {
            logger.Information("{requestId} {platformId} {statusCode}", "template", "platform", 418);
            context.StatusCode = 204;
            context.ElapsedMilliseconds = 10;
            logger.Information("{statusCode} {elapsedMs}", 418, -1);
        }

        var first = sink.Events[0].Properties;
        Assert.Equal("ambient", Assert.IsType<ScalarValue>(first["requestId"]).Value);
        Assert.Equal("platform", Assert.IsType<ScalarValue>(first["platformId"]).Value);
        Assert.Equal(418, Assert.IsType<ScalarValue>(first["statusCode"]).Value);
        Assert.Equal(0L, Assert.IsType<ScalarValue>(first["elapsedMs"]).Value);
        var second = sink.Events[1].Properties;
        Assert.Equal(204, Assert.IsType<ScalarValue>(second["statusCode"]).Value);
        Assert.Equal(10L, Assert.IsType<ScalarValue>(second["elapsedMs"]).Value);
    }

    [Fact]
    public void ConfigureMsSerilog_ShouldLeaveRequestPropertiesAbsent_WhenNoAmbientContextExists()
    {
        var sink = new CollectingSink();
        var builder = Host.CreateApplicationBuilder();
        builder.ConfigureMsSerilog(options =>
        {
            options.ReadFromConfiguration = false;
            options.UseConsoleSink = false;
            options.ConfigureLogger = (_, loggerConfiguration) =>
            {
                loggerConfiguration.WriteTo.Sink(sink);
            };
        });

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILogger<MsSerilogProviderTests>>();

        logger.LogInformation("hello without context");

        sink.Events.Should().ContainSingle();
        var logEvent = sink.Events[0];
        logEvent.Properties.Should().NotContainKey(RequestLogDefaults.RequestIdPropertyName);
        logEvent.Properties.Should().NotContainKey(RequestLogDefaults.PlatformIdPropertyName);
        logEvent.Properties.Should().NotContainKey(RequestLogDefaults.UserFlagPropertyName);
        logEvent.Properties.Should().NotContainKey(RequestLogDefaults.ElapsedMillisecondsPropertyName);
    }

    [Fact]
    public void ConfigureMsSerilog_ShouldSuppressAllLogs_WhenMinimumLevelIsNone()
    {
        var sink = new CollectingSink();
        var builder = Host.CreateApplicationBuilder();
        builder.ConfigureMsSerilog(options =>
        {
            options.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.None;
            options.ReadFromConfiguration = false;
            options.UseConsoleSink = false;
            options.ConfigureLogger = (_, loggerConfiguration) =>
            {
                loggerConfiguration.MinimumLevel.Override("Explicit.Override", LogEventLevel.Verbose);
                loggerConfiguration.WriteTo.Sink(sink);
            };
        });

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILogger<MsSerilogProviderTests>>();

        logger.LogCritical("should not be written");

        var nativeLogger = host.Services.GetRequiredService<global::Serilog.ILogger>();
        Assert.False(nativeLogger.IsEnabled(LogEventLevel.Fatal));
        var overridden = nativeLogger.ForContext("SourceContext", "Explicit.Override.Category");
        Assert.True(overridden.IsEnabled(LogEventLevel.Fatal));
        overridden.Fatal("also filtered");
        sink.Events.Should().BeEmpty();
    }

    [Fact]
    public void ConfigureMsSerilog_HostBuilderOverload_ShouldWriteWithConfiguredSink()
    {
        var sink = new CollectingSink();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureMsSerilog(options =>
            {
                options.ReadFromConfiguration = false;
                options.UseConsoleSink = false;
                options.ConfigureLogger = (_, loggerConfiguration) =>
                {
                    loggerConfiguration.WriteTo.Sink(sink);
                };
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<MsSerilogProviderTests>>();

        logger.LogWarning("hello from host builder");

        sink.Events.Should().ContainSingle();
        sink.Events[0].Level.Should().Be(LogEventLevel.Warning);
        sink.Events[0].RenderMessage().Should().Be("hello from host builder");
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
        }
    }
}
