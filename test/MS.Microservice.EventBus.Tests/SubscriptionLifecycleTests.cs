using MS.Microservice.EventBus.Abstractions;
using MS.Microservice.EventBus.Events;

namespace MS.Microservice.EventBus.Tests;

public sealed class SubscriptionLifecycleTests
{
    [Fact]
    public void CollidingNamesShareHandlersAndKeepTheFirstTypeUntilTheLastRemoval()
    {
        var manager = new InMemoryEventBusSubscriptionsManager();
        const string name = nameof(Sales.Changed);
        manager.AddSubscription<Sales.Changed, Handler<IntegrationEvent>>();
        Assert.Throws<ArgumentException>(() => manager.AddSubscription<Stock.Changed, Handler<IntegrationEvent>>());
        manager.AddSubscription<Stock.Changed, Handler<Stock.Changed>>();
        Assert.Null(manager.GetEventTypeByName("changed"));

        // The shared handler can be removed through either event with this key.
        manager.RemoveSubscription<Stock.Changed, Handler<IntegrationEvent>>();
        manager.RemoveSubscription<Sales.Changed, Handler<IntegrationEvent>>();
        manager.RemoveSubscription<Sales.Changed, Handler<Sales.Changed>>();
        Assert.Same(typeof(Sales.Changed), manager.GetEventTypeByName(name));
        Assert.Throws<ArgumentException>(() => manager.AddSubscription<Stock.Changed, Handler<Stock.Changed>>());

        manager.RemoveSubscription<Stock.Changed, Handler<Stock.Changed>>();
        Assert.Null(manager.GetEventTypeByName(name));
        manager.AddSubscription<Stock.Changed, Handler<IntegrationEvent>>();
        Assert.Same(typeof(Stock.Changed), manager.GetEventTypeByName(name));
    }

    [Fact]
    public void ConcurrentRegistrationHasOneWinnerAndOtherHandlersSurviveConcurrentRemoval()
    {
        var manager = new InMemoryEventBusSubscriptionsManager();
        int successfulAdds = 0;
        Parallel.For(0, 32, _ =>
        {
            try
            {
                manager.AddSubscription<Sales.Changed, Handler<Sales.Changed>>();
                Interlocked.Increment(ref successfulAdds);
            }
            catch (ArgumentException)
            {
                // Another caller registered this handler first.
            }
            Assert.Same(typeof(Sales.Changed), manager.GetEventTypeByName(nameof(Sales.Changed)));
        });
        Assert.Equal(1, successfulAdds);

        manager.AddSubscription<Sales.Changed, Handler<IntegrationEvent>>();
        Parallel.For(0, 32, _ =>
        {
            manager.RemoveSubscription<Sales.Changed, Handler<Sales.Changed>>();
            Assert.Same(typeof(Sales.Changed), manager.GetEventTypeByName(nameof(Sales.Changed)));
        });
        manager.RemoveSubscription<Sales.Changed, Handler<IntegrationEvent>>();
        Assert.Null(manager.GetEventTypeByName(nameof(Sales.Changed)));
    }

    [Fact]
    public void SharedAdditionalHandlerIsIndependentForEachEventAndCanBeRegisteredAgainAfterClear()
    {
        var manager = new InMemoryEventBusSubscriptionsManager();
        manager.AddSubscription<OrderCreatedEvent, Handler<OrderCreatedEvent>>();
        manager.AddSubscription<OrderCancelledEvent, Handler<OrderCancelledEvent>>();
        manager.AddSubscription<OrderCreatedEvent, Handler<IntegrationEvent>>();
        manager.AddSubscription<OrderCancelledEvent, Handler<IntegrationEvent>>();

        manager.RemoveSubscription<OrderCreatedEvent, Handler<IntegrationEvent>>();
        manager.RemoveSubscription<OrderCreatedEvent, Handler<OrderCreatedEvent>>();
        Assert.Null(manager.GetEventTypeByName(nameof(OrderCreatedEvent)));
        Assert.Throws<ArgumentException>(() => manager.AddSubscription<OrderCancelledEvent, Handler<IntegrationEvent>>());

        manager.Clear();
        manager.AddSubscription<OrderCancelledEvent, Handler<OrderCancelledEvent>>();
        manager.AddSubscription<OrderCancelledEvent, Handler<IntegrationEvent>>();
        manager.RemoveSubscription<OrderCancelledEvent, Handler<OrderCancelledEvent>>();
        Assert.Same(typeof(OrderCancelledEvent), manager.GetEventTypeByName(nameof(OrderCancelledEvent)));
        manager.RemoveSubscription<OrderCancelledEvent, Handler<IntegrationEvent>>();
        Assert.Null(manager.GetEventTypeByName(nameof(OrderCancelledEvent)));
    }

    [Fact]
    public void ClearRacingWithAnAddLeavesOnlyRegistrationsMadeAfterClear()
    {
        var manager = new InMemoryEventBusSubscriptionsManager();
        for (int i = 0; i < 32; i++)
        {
            manager.AddSubscription<Sales.Changed, Handler<Sales.Changed>>();
            Parallel.Invoke(manager.Clear, manager.AddSubscription<Stock.Changed, Handler<Stock.Changed>>);

            // Clear ran last, or the Stock registration established a fresh binding.
            var type = manager.GetEventTypeByName(nameof(Stock.Changed));
            Assert.True(type is null || type == typeof(Stock.Changed));
            manager.RemoveSubscription<Stock.Changed, Handler<Stock.Changed>>();
            Assert.Null(manager.GetEventTypeByName(nameof(Stock.Changed)));
        }
    }

    private static class Sales { internal sealed class Changed : IntegrationEvent; }
    private static class Stock { internal sealed class Changed : IntegrationEvent; }

    private sealed class Handler<T> : IIntegrationEventHandler<T> where T : IntegrationEvent
    {
        public Task Handle(T @event) => Task.CompletedTask;
    }
}
