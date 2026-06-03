using MS.Microservice.EventBus.Abstractions;
using MS.Microservice.EventBus.Events;

namespace MS.Microservice.EventBus;

/// <summary>
/// Manages in-memory mappings between integration events and their handlers.
/// </summary>
public interface IEventBusSubscriptionsManager
{
    void AddSubscription<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>;

    void RemoveSubscription<TEvent, THandler>()
        where THandler : IIntegrationEventHandler<TEvent>
        where TEvent : IntegrationEvent;

    Type? GetEventTypeByName(string eventName);

    void Clear();

    string GetEventKey<TEvent>()
        where TEvent : IntegrationEvent;
}
