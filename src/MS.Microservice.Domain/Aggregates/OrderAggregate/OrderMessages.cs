using MS.Microservice.Domain.EventSourcing;
using System;

namespace MS.Microservice.Domain.Aggregates.OrderAggregate
{
    public abstract record OrderCommand(Guid OrderId);

    public sealed record CreateOrder(Guid OrderId, string CustomerId, string Currency) : OrderCommand(OrderId);

    public sealed record AddOrderItem(Guid OrderId, string ProductId, decimal UnitPrice, int Quantity) : OrderCommand(OrderId);

    public sealed record RemoveOrderItem(Guid OrderId, string ProductId, int Quantity) : OrderCommand(OrderId);

    public sealed record ConfirmOrder(Guid OrderId) : OrderCommand(OrderId);

    public sealed record CancelOrder(Guid OrderId, string Reason) : OrderCommand(OrderId);

    public abstract record OrderEvent(Guid OrderId) : IEventSourcedEvent;

    public sealed record OrderCreated(Guid OrderId, string CustomerId, string Currency) : OrderEvent(OrderId);

    public sealed record OrderItemAdded(Guid OrderId, string ProductId, decimal UnitPrice, int Quantity) : OrderEvent(OrderId);

    public sealed record OrderItemRemoved(Guid OrderId, string ProductId, decimal UnitPrice, int Quantity) : OrderEvent(OrderId);

    public sealed record OrderConfirmed(Guid OrderId) : OrderEvent(OrderId);

    public sealed record OrderCancelled(Guid OrderId, string Reason) : OrderEvent(OrderId);
}
