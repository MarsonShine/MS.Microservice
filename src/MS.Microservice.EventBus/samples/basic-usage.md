## Basic Usage

```csharp
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.EventBus;
using MS.Microservice.EventBus.Abstractions;
using MS.Microservice.EventBus.Events;

var services = new ServiceCollection();
services.AddSingleton<IEventBusSubscriptionsManager, InMemoryEventBusSubscriptionsManager>();
services.AddSingleton<IEventBus, RabbitMqEventBus>();

public sealed class PaymentSucceededEvent(Guid orderId) : IntegrationEvent
{
    public Guid OrderId { get; } = orderId;
}

public sealed class PaymentSucceededHandler : IIntegrationEventHandler<PaymentSucceededEvent>
{
    public Task Handle(PaymentSucceededEvent @event)
    {
        Console.WriteLine($"Payment succeeded for order {@event.OrderId}");
        return Task.CompletedTask;
    }
}

public sealed class RabbitMqEventBus(IEventBusSubscriptionsManager subscriptionsManager) : IEventBus
{
    public void Publish(IntegrationEvent @event)
    {
        // Publish the serialized event to RabbitMQ.
    }

    public void Subscribe<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>
    {
        subscriptionsManager.AddSubscription<TEvent, THandler>();
    }

    public void Unsubscribe<TEvent, THandler>()
        where THandler : IIntegrationEventHandler<TEvent>
        where TEvent : IntegrationEvent
    {
        subscriptionsManager.RemoveSubscription<TEvent, THandler>();
    }
}
```

This keeps transport concerns in the concrete bus implementation and leaves subscription bookkeeping to `InMemoryEventBusSubscriptionsManager`.
