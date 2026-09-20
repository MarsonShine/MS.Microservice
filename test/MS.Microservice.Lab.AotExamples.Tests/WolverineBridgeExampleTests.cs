using Microsoft.EntityFrameworkCore;
using MS.Microservice.Messaging;
using MS.Microservice.Messaging.Wolverine;
using global::Wolverine;
using Xunit;
using LegacyExample = MS.Microservice.Lab.AotExamples.Legacy.Messaging.WolverineBridgeExample;
using StaticExample = MS.Microservice.Lab.AotExamples.Static.Messaging.WolverineBridgeExample;
using ExampleEvent = MS.Microservice.Lab.AotExamples.Tests.MessagingContractExampleTests.ExampleEvent;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class WolverineBridgeExampleTests
{
    [Theory]
    [InlineData("example.changed", 1)]
    [InlineData("example.updated", 2)]
    [InlineData("example.latest", int.MaxValue)]
    public void ExplicitGenericCallPreservesBridgeAndDurableRoute(string name, int version)
    {
        var contracts = new MessageContractRegistry([
            MessageContract.For<ExampleEvent>(name, MessagingExampleJsonContext.Default.ExampleEvent, version)]);
        var legacy = Options();
        var current = Options();

        LegacyExample.Configure<ExampleContext>(legacy, contracts);
        StaticExample.Configure<ExampleEvent, ExampleContext>(current, contracts);

        var expected = new Uri($"ms-rabbitmq://exchange/{name}.v{version}");
        var oldEndpoint = Assert.Single(legacy.Transports.AllEndpoints(), endpoint => endpoint.Uri == expected);
        var newEndpoint = Assert.Single(current.Transports.AllEndpoints(), endpoint => endpoint.Uri == expected);
        Assert.Equal(oldEndpoint.Mode, newEndpoint.Mode);
        Assert.Equal(global::Wolverine.Configuration.EndpointMode.Durable, newEndpoint.Mode);
        Assert.Equal(Assert.Single(oldEndpoint.Subscriptions).Match, Assert.Single(newEndpoint.Subscriptions).Match);

        // Exercise Wolverine's real discovery seam without connecting to a broker or database.
        var discover = typeof(global::Wolverine.Configuration.HandlerDiscovery).GetMethod("FindCalls",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        foreach (var options in new[] { legacy, current })
        {
            var calls = ((Type, System.Reflection.MethodInfo)[])discover.Invoke(options.Discovery, [options])!;
            Assert.Contains(calls, call => call.Item1 == typeof(WolverineIntegrationEventHandler<ExampleEvent, ExampleContext>));
        }
    }

    private static WolverineOptions Options()
    {
        var options = new WolverineOptions();
        options.Transports.Add(new ConfirmedRabbitMqTransport());
        return options;
    }

    public sealed class ExampleContext(DbContextOptions<ExampleContext> options) : DbContext(options);
}
