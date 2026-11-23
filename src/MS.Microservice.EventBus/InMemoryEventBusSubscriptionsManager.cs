using System.Collections.Concurrent;
using MS.Microservice.EventBus.Abstractions;
using MS.Microservice.EventBus.Events;
using Microsoft.Extensions.DependencyInjection;

namespace MS.Microservice.EventBus;

/// <summary>
/// Stores subscriptions in memory using constant-time lookups by event name and handler type.
/// </summary>
public sealed class InMemoryEventBusSubscriptionsManager : IEventBusSubscriptionsManager
{
    private readonly object _writes = new();
    // Readers see the immutable EventType or a published snapshot. Writers own all mutable membership state.
    private volatile ConcurrentDictionary<string, Subscription>? _subscriptions;
    // Same-name CLR types share a binding. Always use its original EventType for additional handlers.
    private Dictionary<HandlerKey, LinkedListNode<HandlerRegistration>>? _additionalHandlers;

    public void AddSubscription<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>
    {
        var eventName = GetEventKey<TEvent>();
        var handler = Registration<TEvent, THandler>.Value;
        lock (_writes)
        {
            // Writes use one gate, so the name index only needs a single write stripe.
            var subscriptions = _subscriptions ??= new(concurrencyLevel: 1, capacity: 16, comparer: StringComparer.Ordinal);
            if (!subscriptions.TryGetValue(eventName, out var subscription))
            {
                subscriptions[eventName] = new Subscription(handler);
                return;
            }
            var key = new HandlerKey(subscription.EventType, handler.HandlerType);
            if (subscription.FirstHandler?.HandlerType == handler.HandlerType || _additionalHandlers?.ContainsKey(key) == true)
            {
                throw new ArgumentException(
                    $"Handler type {handler.HandlerType.Name} is already registered for '{eventName}'.");
            }
            var node = new LinkedListNode<HandlerRegistration>(handler);
            subscription.Additional ??= [];
            (_additionalHandlers ??= []).Add(key, node);
            subscription.Additional.AddLast(node);
            subscription.Snapshot = null;
        }
    }

    public void RemoveSubscription<TEvent, THandler>()
        where THandler : IIntegrationEventHandler<TEvent>
        where TEvent : IntegrationEvent
    {
        var eventName = GetEventKey<TEvent>();
        lock (_writes)
        {
            var subscriptions = _subscriptions;
            if (subscriptions is null || !subscriptions.TryGetValue(eventName, out var subscription))
                return;
            if (subscription.FirstHandler?.HandlerType == typeof(THandler))
                subscription.FirstHandler = null;
            else if (_additionalHandlers is not null && _additionalHandlers.Remove(new(subscription.EventType, typeof(THandler)), out var node))
                subscription.Additional!.Remove(node);
            else
                return;
            subscription.Snapshot = null;
            if (subscription.FirstHandler is null && subscription.Additional is not { Count: > 0 })
                subscriptions.TryRemove(eventName, out _);
        }
    }

    public Type? GetEventTypeByName(string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        var subscriptions = _subscriptions;
        return subscriptions is not null && subscriptions.TryGetValue(eventName, out var subscription)
            ? subscription.EventType : null;
    }

    public void Clear()
    {
        lock (_writes)
        {
            _subscriptions = null;
            _additionalHandlers = null;
        }
    }

    public string GetEventKey<TEvent>()
        where TEvent : IntegrationEvent => typeof(TEvent).Name;

    private readonly record struct HandlerKey(Type EventType, Type HandlerType);

    internal sealed record HandlerRegistration(Type EventType, Type HandlerType, Func<IServiceProvider, IntegrationEvent, Task> Invoke);

    private static class Registration<TEvent, THandler>
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>
    {
        internal static readonly HandlerRegistration Value = new(typeof(TEvent), typeof(THandler),
            static (services, message) => services.GetRequiredService<THandler>().Handle((TEvent)message));
    }

    private sealed record DispatchSnapshot(Type EventType, HandlerRegistration[] Handlers);

    private sealed class Subscription(HandlerRegistration handler)
    {
        public Type EventType { get; } = handler.EventType;
        public HandlerRegistration? FirstHandler { get; set; } = handler;
        public LinkedList<HandlerRegistration>? Additional { get; set; }
        public volatile DispatchSnapshot? Snapshot;
    }

    internal HandlerRegistration[] GetDispatchSnapshot(Type eventType)
    {
        var name = eventType.Name;
        var subscriptions = _subscriptions;
        if (subscriptions is null || !subscriptions.TryGetValue(name, out var entry)) return [];
        var cached = entry.Snapshot;
        if (cached?.EventType == eventType) return cached.Handlers;

        lock (_writes)
        {
            // A writer may have removed, cleared or rebound this name while we waited.
            if (_subscriptions is null || !_subscriptions.TryGetValue(name, out entry)) return [];
            cached = entry.Snapshot;
            if (cached?.EventType == eventType) return cached.Handlers;

            List<HandlerRegistration> matches = [];
            if (entry.FirstHandler is { } first && first.EventType == eventType) matches.Add(first);
            if (entry.Additional is not null)
                foreach (var handler in entry.Additional)
                    if (handler.EventType == eventType) matches.Add(handler);
            if (matches.Count == 0) return [];
            var handlers = matches.ToArray();
            entry.Snapshot = new DispatchSnapshot(eventType, handlers);
            return handlers;
        }
    }
}
