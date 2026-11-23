using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.EventBus.Abstractions;
using MS.Microservice.EventBus.Events;

namespace MS.Microservice.EventBus;

/// <summary>Dispatches in-process events to scoped handlers in registration order.</summary>
public sealed class InMemoryEventBus(
    InMemoryEventBusSubscriptionsManager subscriptions, IServiceScopeFactory scopes) : IEventbus
{
    public void Subscribe<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>
        => subscriptions.AddSubscription<TEvent, THandler>();

    public void Unsubscribe<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>
        => subscriptions.RemoveSubscription<TEvent, THandler>();

    public void Publish(IntegrationEvent @event)
        // A handler can capture its synchronization context. Keep the synchronous caller's context free of handler continuations.
        => Task.Run(() => PublishAsync(@event)).GetAwaiter().GetResult();

    public async Task PublishAsync(IntegrationEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var handlers = subscriptions.GetDispatchSnapshot(@event.GetType());
        if (handlers.Length == 0) return;

        await using var scope = scopes.CreateAsyncScope();
        foreach (var handler in handlers)
            await handler.Invoke(scope.ServiceProvider, @event).ConfigureAwait(false);
    }
}
