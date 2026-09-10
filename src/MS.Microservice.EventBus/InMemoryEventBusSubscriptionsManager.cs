using MS.Microservice.EventBus.Abstractions;
using MS.Microservice.EventBus.Events;

namespace MS.Microservice.EventBus;

/// <summary>
/// Stores subscriptions in memory using constant-time lookups by event name and handler type.
/// </summary>
public sealed class InMemoryEventBusSubscriptionsManager : IEventBusSubscriptionsManager
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, HashSet<Type>> _handlerTypesByEventName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> _eventTypesByName = new(StringComparer.Ordinal);

    public void AddSubscription<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>
    {
        var eventName = GetEventKey<TEvent>();
        var handlerType = typeof(THandler);

        lock (_syncRoot)
        {
            if (!_handlerTypesByEventName.TryGetValue(eventName, out var handlerTypes))
            {
                handlerTypes = [];
                _handlerTypesByEventName[eventName] = handlerTypes;
            }

            if (!handlerTypes.Add(handlerType))
            {
                throw new ArgumentException(
                    $"Handler type {handlerType.Name} is already registered for '{eventName}'.");
            }

            _eventTypesByName.TryAdd(eventName, typeof(TEvent));
        }
    }

    public void RemoveSubscription<TEvent, THandler>()
        where THandler : IIntegrationEventHandler<TEvent>
        where TEvent : IntegrationEvent
    {
        var eventName = GetEventKey<TEvent>();

        lock (_syncRoot)
        {
            if (!_handlerTypesByEventName.TryGetValue(eventName, out var handlerTypes))
            {
                return;
            }

            if (!handlerTypes.Remove(typeof(THandler)))
            {
                return;
            }

            if (handlerTypes.Count > 0)
            {
                return;
            }

            _handlerTypesByEventName.Remove(eventName);
            _eventTypesByName.Remove(eventName);
        }
    }

    public Type? GetEventTypeByName(string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        lock (_syncRoot)
        {
            return _eventTypesByName.TryGetValue(eventName, out var eventType)
                ? eventType
                : null;
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _handlerTypesByEventName.Clear();
            _eventTypesByName.Clear();
        }
    }

    public string GetEventKey<TEvent>()
        where TEvent : IntegrationEvent => typeof(TEvent).Name;
}
