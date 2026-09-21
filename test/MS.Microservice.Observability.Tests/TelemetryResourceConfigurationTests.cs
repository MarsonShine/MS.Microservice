using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MS.Microservice.Infrastructure.Telemetry;
using MS.Microservice.Infrastructure.Telemetry.Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.Telemetry;

public sealed class TelemetryResourceConfigurationTests
{
    [Fact]
    public void AddMsOpenTelemetry_BindsResourceIdentityFromConfiguration()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["OpenTelemetry:ServiceName"] = "orders-api",
            ["OpenTelemetry:ServiceNamespace"] = "platform",
            ["OpenTelemetry:ServiceVersion"] = "2.3.4",
            ["OpenTelemetry:ServiceInstanceId"] = "node-7",
            ["OpenTelemetry:EnvironmentName"] = "Staging",
            ["OpenTelemetry:ActivitySourceName"] = "platform.orders"
        });
        var services = new ServiceCollection();

        services.AddMsOpenTelemetry(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TelemetryResourceOptions>>().Value;
        Assert.Equal("orders-api", options.ServiceName);
        Assert.Equal("platform", options.ServiceNamespace);
        Assert.Equal("2.3.4", options.ServiceVersion);
        Assert.Equal("node-7", options.ServiceInstanceId);
        Assert.Equal("Staging", options.EnvironmentName);
        Assert.Equal("platform.orders", options.ActivitySourceName);
    }

    [Theory]
    [InlineData("OpenTelemetry:ServiceName")]
    [InlineData("OpenTelemetry:ServiceVersion")]
    [InlineData("OpenTelemetry:EnvironmentName")]
    [InlineData("OpenTelemetry:ActivitySourceName")]
    public void AddMsOpenTelemetry_MissingRequiredIdentity_Throws(string key)
    {
        var values = ValidValues();
        values[key] = " ";
        var services = new ServiceCollection();

        var action = () => services.AddMsOpenTelemetry(CreateConfiguration(values));

        Assert.Throws<OptionsValidationException>(action);
    }

    [Theory]
    [InlineData("MS.Microservice.Messaging")]
    [InlineData("Wolverine")]
    [InlineData("MS.Microservice.AI")]
    public void DefaultPipelineCollectsComponentSourcesWithoutImplicitExporters(string source)
    {
        var services = new ServiceCollection();
        services.AddMsOpenTelemetry(CreateConfiguration(ValidValues()));
        using var provider = services.BuildServiceProvider();
        using var tracer = provider.GetRequiredService<OpenTelemetry.Trace.TracerProvider>();
        using var activitySource = new System.Diagnostics.ActivitySource(source);
        using var activity = activitySource.StartActivity("component-operation");
        Assert.NotNull(activity);
        var options = provider.GetRequiredService<IOptions<TelemetryResourceOptions>>().Value;
        Assert.False(options.ConsoleExporterEnabled);
        Assert.False(options.OtlpExporterEnabled);
    }

    [Fact]
    public void MissingSectionPreservesResourceAndExporterDefaults()
    {
        var services = new ServiceCollection();
        services.AddMsOpenTelemetry(CreateConfiguration([]));
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TelemetryResourceOptions>>().Value;
        Assert.Equal("MS.Microservice", options.ServiceName);
        Assert.Equal("Production", options.EnvironmentName);
        Assert.Null(options.ServiceInstanceId);
        Assert.False(options.ConsoleExporterEnabled);
        Assert.False(options.OtlpExporterEnabled);
    }

    [Theory]
    [InlineData("true", "FALSE", true, false)]
    [InlineData("false", "True", false, true)]
    public void ExporterFlagsUseBooleanConfigurationRules(string console, string otlp, bool expectedConsole, bool expectedOtlp)
    {
        var values = ValidValues();
        values["OpenTelemetry:ConsoleExporterEnabled"] = console;
        values["OpenTelemetry:OtlpExporterEnabled"] = otlp;
        var services = new ServiceCollection();
        services.AddMsOpenTelemetry(CreateConfiguration(values));
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TelemetryResourceOptions>>().Value;
        Assert.Equal(expectedConsole, options.ConsoleExporterEnabled);
        Assert.Equal(expectedOtlp, options.OtlpExporterEnabled);
    }

    [Fact]
    public void InvalidBooleanStillFailsAtRegistration()
    {
        var values = ValidValues();
        values["OpenTelemetry:ConsoleExporterEnabled"] = "enabled";
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMsOpenTelemetry(CreateConfiguration(values)));
    }

    [Fact]
    public void BoundOptionsMonitorStillReceivesConfigurationReloads()
    {
        var configuration = (IConfigurationRoot)CreateConfiguration(ValidValues());
        var services = new ServiceCollection();
        services.AddMsOpenTelemetry(configuration);
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<TelemetryResourceOptions>>();
        Assert.Equal("service", monitor.CurrentValue.ServiceName);
        configuration["OpenTelemetry:ServiceName"] = "reloaded";
        configuration.Reload();
        Assert.Equal("reloaded", monitor.CurrentValue.ServiceName);
        Assert.Equal("service", monitor.CurrentValue.ActivitySourceName);
    }
    private static Dictionary<string, string?> ValidValues() => new()
    {
        ["OpenTelemetry:ServiceName"] = "service",
        ["OpenTelemetry:ServiceVersion"] = "1.0.0",
        ["OpenTelemetry:EnvironmentName"] = "Test",
        ["OpenTelemetry:ActivitySourceName"] = "service"
    };

    private static IConfiguration CreateConfiguration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
