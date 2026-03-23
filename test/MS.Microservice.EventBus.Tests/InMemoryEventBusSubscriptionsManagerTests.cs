using MS.Microservice.EventBus.Abstractions;
using MS.Microservice.EventBus.Events;
using System;
using System.Threading.Tasks;

namespace MS.Microservice.EventBus.Tests
{
    // Test event types
    public class OrderCreatedEvent : IntegrationEvent { }
    public class OrderCancelledEvent : IntegrationEvent { }

    // Test handler types
    public class OrderCreatedEventHandler : IIntegrationEventHandler<OrderCreatedEvent>
    {
        public Task Handle(OrderCreatedEvent @event) => Task.CompletedTask;
    }

    public class OrderCreatedEventHandler2 : IIntegrationEventHandler<OrderCreatedEvent>
    {
        public Task Handle(OrderCreatedEvent @event) => Task.CompletedTask;
    }

    public class OrderCancelledEventHandler : IIntegrationEventHandler<OrderCancelledEvent>
    {
        public Task Handle(OrderCancelledEvent @event) => Task.CompletedTask;
    }

    public class InMemoryEventBusSubscriptionsManagerTests
    {
        [Fact]
        public void AddSubscription_RegistersHandler()
        {
            var manager = new InMemoryEventBusSubscriptionsManager();
            manager.AddSubscription<OrderCreatedEvent, OrderCreatedEventHandler>();

            var eventType = manager.GetEventTypeByName(nameof(OrderCreatedEvent));
            Assert.NotNull(eventType);
            Assert.Equal(typeof(OrderCreatedEvent), eventType);
        }

        [Fact]
        public void AddSubscription_DuplicateHandler_ThrowsArgumentException()
        {
            var manager = new InMemoryEventBusSubscriptionsManager();
            manager.AddSubscription<OrderCreatedEvent, OrderCreatedEventHandler>();

            Assert.Throws<ArgumentException>(() =>
                manager.AddSubscription<OrderCreatedEvent, OrderCreatedEventHandler>());
        }

        [Fact]
        public void AddSubscription_MultipleHandlersForSameEvent_Succeeds()
        {
            var manager = new InMemoryEventBusSubscriptionsManager();
            manager.AddSubscription<OrderCreatedEvent, OrderCreatedEventHandler>();
            manager.AddSubscription<OrderCreatedEvent, OrderCreatedEventHandler2>();

            // Both registered, event type still resolvable
            var eventType = manager.GetEventTypeByName(nameof(OrderCreatedEvent));
            Assert.Equal(typeof(OrderCreatedEvent), eventType);
        }

        [Fact]
        public void GetEventKey_ReturnsTypeName()
        {
            var manager = new InMemoryEventBusSubscriptionsManager();
            var key = manager.GetEventKey<OrderCreatedEvent>();
            Assert.Equal("OrderCreatedEvent", key);
        }

        [Fact]
        public void GetEventTypeByName_UnknownEvent_ReturnsNull()
        {
            var manager = new InMemoryEventBusSubscriptionsManager();
            var eventType = manager.GetEventTypeByName("NonExistentEvent");
            Assert.Null(eventType);
        }

        [Fact]
        public void RemoveSubscription_RemovesHandler()
        {
            var manager = new InMemoryEventBusSubscriptionsManager();
            manager.AddSubscription<OrderCreatedEvent, OrderCreatedEventHandler>();

            manager.RemoveSubscription<OrderCreatedEvent, OrderCreatedEventHandler>();

            // After removing the only handler, the event type should also be removed
            var eventType = manager.GetEventTypeByName(nameof(OrderCreatedEvent));
            Assert.Null(eventType);
        }

        [Fact]
        public void RemoveSubscription_OneOfMultipleHandlers_KeepsEvent()
        {
            var manager = new InMemoryEventBusSubscriptionsManager();
            manager.AddSubscription<OrderCreatedEvent, OrderCreatedEventHandler>();
            manager.AddSubscription<OrderCreatedEvent, OrderCreatedEventHandler2>();

            manager.RemoveSubscription<OrderCreatedEvent, OrderCreatedEventHandler>();

            // Event type should still be registered (handler2 remains)
            var eventType = manager.GetEventTypeByName(nameof(OrderCreatedEvent));
            Assert.NotNull(eventType);
        }

        [Fact]
        public void RemoveSubscription_NonExistentEvent_DoesNotThrow()
        {
            var manager = new InMemoryEventBusSubscriptionsManager();
            // Should not throw when removing from non-existent event
            manager.RemoveSubscription<OrderCreatedEvent, OrderCreatedEventHandler>();
        }

        [Fact]
        public void Clear_RemovesAllSubscriptions()
        {
            var manager = new InMemoryEventBusSubscriptionsManager();
            manager.AddSubscription<OrderCreatedEvent, OrderCreatedEventHandler>();
            manager.AddSubscription<OrderCancelledEvent, OrderCancelledEventHandler>();

            manager.Clear();

            Assert.Null(manager.GetEventTypeByName(nameof(OrderCreatedEvent)));
            Assert.Null(manager.GetEventTypeByName(nameof(OrderCancelledEvent)));
        }

        [Fact]
        public void MultipleEvents_IndependentSubscriptions()
        {
            var manager = new InMemoryEventBusSubscriptionsManager();
            manager.AddSubscription<OrderCreatedEvent, OrderCreatedEventHandler>();
            manager.AddSubscription<OrderCancelledEvent, OrderCancelledEventHandler>();

            Assert.NotNull(manager.GetEventTypeByName(nameof(OrderCreatedEvent)));
            Assert.NotNull(manager.GetEventTypeByName(nameof(OrderCancelledEvent)));

            manager.RemoveSubscription<OrderCreatedEvent, OrderCreatedEventHandler>();

            Assert.Null(manager.GetEventTypeByName(nameof(OrderCreatedEvent)));
            Assert.NotNull(manager.GetEventTypeByName(nameof(OrderCancelledEvent)));
        }
    }
}
