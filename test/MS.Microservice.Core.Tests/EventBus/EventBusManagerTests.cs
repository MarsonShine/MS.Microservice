// BEGIN: 8d7f5e3b1f4c
using System.Threading.Tasks;
using MS.Microservice.Core.EventBus;

namespace MS.Microservice.Core.Tests.EventBus
{
    public class EventBusManagerTests
    {
        [Fact]
        public async Task PublishAsync_Should_Invoke_All_Event_Handlers()
        {
            // Arrange
            var eventBus = new EventBusManager();
            var eventHandler1 = new TestEventHandler();
            var eventHandler2 = new TestEventHandler();
            eventBus.Subscribe<TestEvent, TestEventHandler>(eventHandler1);
            eventBus.Subscribe<TestEvent, TestEventHandler>(eventHandler2);
            var testEvent = new TestEvent();

            // Act
            await eventBus.PublishAsync(testEvent);

            // Assert
            Assert.True(eventHandler1.HandledEvent);
            Assert.True(eventHandler2.HandledEvent);
        }

        [Fact]
        public void Subscribe_Should_Add_Event_Handler_To_Event_Handlers_List()
        {
            // Arrange
            var eventBus = new EventBusManager();
            var eventHandler = new TestEventHandler();

            // Act
            eventBus.Subscribe<TestEvent, TestEventHandler>(eventHandler);

            // Assert
            Assert.Contains(eventHandler, eventBus.GetEventHandlers<TestEvent>());
        }

        [Fact]
        public void UnSubscribe_Should_Remove_Event_Handler_From_Event_Handlers_List()
        {
            // Arrange
            var eventBus = new EventBusManager();
            var eventHandler = new TestEventHandler();
            eventBus.Subscribe<TestEvent, TestEventHandler>(eventHandler);

            // Act
            eventBus.UnSubscribe<TestEvent, TestEventHandler>(eventHandler);

            // Assert
            Assert.DoesNotContain(eventHandler, eventBus.GetEventHandlers<TestEvent>());
        }

        private class TestEvent : IEvent { }

        private class TestEventHandler : IEventHandler<TestEvent>
        {
            public bool HandledEvent { get; private set; }

            public Task Handle(TestEvent evt)
            {
                HandledEvent = true;
                return Task.CompletedTask;
            }
        }
    }
}
// END: 8d7f5e3b1f4c