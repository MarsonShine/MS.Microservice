using MS.Microservice.EventBus.Abstractions;
using MS.Microservice.EventBus.Events;

namespace MS.Microservice.EventBus.Tests;

public class EventBusContractTests
{
    [Fact]
    public void LegacyInterface_ShouldRemainAssignableToCanonicalInterface()
    {
        Assert.True(typeof(IEventBus).IsAssignableFrom(typeof(IEventbus)));
    }

    [Fact]
    public async Task AsyncEntryPointSupportsExistingSynchronousAdapters()
    {
        var adapter = new SynchronousAdapter();
        IEventBus bus = adapter;
        var message = new IntegrationEvent();
        await bus.PublishAsync(message);
        Assert.Same(message, adapter.Published);
    }

    private sealed class SynchronousAdapter : IEventbus
    {
        public IntegrationEvent? Published { get; private set; }
        public void Publish(IntegrationEvent @event) => Published = @event;
        public void Subscribe<TEvent, THandler>() where TEvent : IntegrationEvent
            where THandler : IIntegrationEventHandler<TEvent> => throw new NotSupportedException();
        public void Unsubscribe<TEvent, THandler>() where TEvent : IntegrationEvent
            where THandler : IIntegrationEventHandler<TEvent> => throw new NotSupportedException();
    }
}
