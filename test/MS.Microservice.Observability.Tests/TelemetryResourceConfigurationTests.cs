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
