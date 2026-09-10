using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using global::Wolverine;
using global::Wolverine.Runtime;
using Xunit;
using static MS.Microservice.Messaging.Wolverine.Tests.RegistrationTests;

namespace MS.Microservice.Messaging.Wolverine.Tests;

public sealed class HandlerDiscoveryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NeutralHandlerIsReachedOnlyThroughAdapterWhileLocalCommandsRemainDiscoverable(int variant)
    {
        var subscriptions = variant switch
        {
            0 => MessageSubscription.For<Changed, Handler>("audit"),
            1 => MessageSubscription.For<Changed, AuditConsumer>("audit"),
            2 => MessageSubscription.For<Changed, DeferredHandler>("audit"),
            _ => MessageSubscription.For<Changed, InheritedConsumer>("audit")
        };
        var topology = new MessageTopology([MessageContract.For<Changed>("profile.changed")], [subscriptions]);
        var settings = Options();
        var options = new WolverineOptions();
        options.Discovery.IncludeAssembly(typeof(HandlerDiscoveryTests).Assembly);
        WolverineMessagingExtensions.ConfigureWolverineMessaging<TestContext>(options, topology, settings);
        // Fixed-version discovery seam: exercise the real scanner without starting any transports.
        var discover = typeof(global::Wolverine.Configuration.HandlerDiscovery).GetMethod("FindCalls",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var calls = ((Type, System.Reflection.MethodInfo)[])discover.Invoke(options.Discovery, [options])!;
        Assert.Single(calls, call => call.Item2.GetParameters()[0].ParameterType == typeof(Changed));
        Assert.Single(calls, call => call.Item2.GetParameters()[0].ParameterType == typeof(LocalCommand));
    }

    public sealed record LocalCommand(string Value);
    public sealed class LocalCommandHandler { public void Handle(LocalCommand command) { } }
    public sealed class AuditConsumer : IIntegrationEventHandler<Changed>
    {
        public Task HandleAsync(Changed message, MessageContext context, CancellationToken token) => Task.CompletedTask;
    }
    public sealed class DeferredHandler : IIntegrationEventHandler<Changed>
    {
        public Task HandleAsync(Changed message, MessageContext context, CancellationToken token) => Task.CompletedTask;
    }
    public abstract class SharedHandlerBase : IIntegrationEventHandler<Changed>
    {
        public Task HandleAsync(Changed message, MessageContext context, CancellationToken token) => Task.CompletedTask;
    }
    public sealed class InheritedConsumer : SharedHandlerBase;
}
