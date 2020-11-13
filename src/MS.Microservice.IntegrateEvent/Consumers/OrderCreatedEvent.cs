namespace MS.Microservice.IntegrateEvent.Consumers
{
    using MS.Microservice.IntegrateEvent.Contracts;
    using System;
    public class OrderCreatedEvent : IOrderCreatedEvent
    {
        public OrderCreatedEvent(string orderNumber, string orderName) {
            OrderNumber = orderNumber;
            OrderName = orderName;
            CreationTime = DateTime.UtcNow;
        }
        public string OrderNumber { get; }

        public string OrderName { get; }

        public DateTime CreationTime { get; }
    }
}
