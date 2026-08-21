using FluentAssertions;
using MS.Microservice.Domain;
using MS.Microservice.Infrastructure.Messaging;
using NSubstitute;
using Wolverine;
using Xunit;
using CanonicalIntegrationEvent = MS.Microservice.Core.Messaging.IntegrationEvent;
using IIntegrationEvent = MS.Microservice.Core.Messaging.IIntegrationEvent;

namespace MS.Microservice.Infrastructure.Tests.DomainEvents;

public sealed class DomainEventDispatchTests
{
    [Fact]
    public async Task WolverineDomainEventDispatcher_ShouldPublishEachDomainEvent()
    {
        var messageBus = Substitute.For<IMessageBus>();
        messageBus.PublishAsync(Arg.Any<IDomainEvent>(), Arg.Any<DeliveryOptions?>()).Returns(ValueTask.CompletedTask);
        var dispatcher = new WolverineDomainEventDispatcher(messageBus);

        await dispatcher.DispatchAsync([new TestDomainEvent("one"), new TestDomainEvent("two")]);

        await messageBus.Received(2).PublishAsync(Arg.Any<IDomainEvent>(), Arg.Any<DeliveryOptions?>());
    }

    [Fact]
    public async Task WolverineIntegrationEventPublisher_ShouldPublishCanonicalIntegrationEvent()
    {
        var messageBus = Substitute.For<IMessageBus>();
        messageBus.PublishAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<DeliveryOptions?>())
            .Returns(ValueTask.CompletedTask);
        var publisher = new WolverineIntegrationEventPublisher(messageBus);
        var integrationEvent = new TestIntegrationEvent();

        await publisher.PublishAsync(integrationEvent);

        await messageBus.Received(1)
            .PublishAsync(integrationEvent, Arg.Any<DeliveryOptions?>());
    }

    private sealed record TestDomainEvent(string Name) : IDomainEvent;

    private sealed class TestIntegrationEvent : CanonicalIntegrationEvent;

}
