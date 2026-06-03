## MS.Microservice.EventBus

Reusable EventBus contracts and an optimized in-memory subscription manager for integration events.

### What This Package Provides

- `IEventBus` as the canonical event bus contract
- `IEventbus` as a backward-compatible alias for existing consumers
- `IntegrationEvent` as the base type for serialized integration events
- `InMemoryEventBusSubscriptionsManager` for fast in-memory subscription registration and lookup

### Improvements Over The Extracted Version

- Event name to event type lookup is now O(1)
- Subscription registration and removal are protected against concurrent mutations
- Duplicate handler registration is rejected deterministically per event
- `IntegrationEvent` can round-trip cleanly through `System.Text.Json`

### Install

```bash
dotnet add package MS.Microservice.EventBus
```

### Basic Subscription Usage

```csharp
using MS.Microservice.EventBus;
using MS.Microservice.EventBus.Abstractions;
using MS.Microservice.EventBus.Events;

public sealed class OrderCreatedEvent : IntegrationEvent
{
    public OrderCreatedEvent(Guid orderId)
    {
        OrderId = orderId;
    }

    public Guid OrderId { get; }
}

public sealed class OrderCreatedEventHandler : IIntegrationEventHandler<OrderCreatedEvent>
{
    public Task Handle(OrderCreatedEvent @event)
    {
        Console.WriteLine($"Order created: {@event.OrderId}");
        return Task.CompletedTask;
    }
}

var subscriptions = new InMemoryEventBusSubscriptionsManager();
subscriptions.AddSubscription<OrderCreatedEvent, OrderCreatedEventHandler>();

var eventType = subscriptions.GetEventTypeByName(nameof(OrderCreatedEvent));
Console.WriteLine(eventType == typeof(OrderCreatedEvent));
```

### Event Bus Adapter Example

```csharp
using MS.Microservice.EventBus;
using MS.Microservice.EventBus.Abstractions;
using MS.Microservice.EventBus.Events;

public sealed class RabbitMqEventBus(IEventBusSubscriptionsManager subscriptionsManager) : IEventBus
{
    public void Publish(IntegrationEvent @event)
    {
        // Serialize the event and send it to the broker.
    }

    public void Subscribe<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>
    {
        subscriptionsManager.AddSubscription<TEvent, THandler>();
        // Create the broker binding or consumer registration.
    }

    public void Unsubscribe<TEvent, THandler>()
        where THandler : IIntegrationEventHandler<TEvent>
        where TEvent : IntegrationEvent
    {
        subscriptionsManager.RemoveSubscription<TEvent, THandler>();
        // Remove the broker binding if needed.
    }
}
```

### Notes

- `GetEventKey<TEvent>()` uses the CLR type name by default.
- `GetEventTypeByName()` returns `null` for unknown events and throws for null or whitespace names.
- See `samples/basic-usage.md` for a slightly more complete registration example.
