using MS.Microservice.Domain.Aggregates.OrderAggregate;
using MS.Microservice.Domain.EventSourcing;
using System;

namespace MS.Microservice.Infrastructure.EventSourcing.Orders
{
    public static class OrderReadModelProjection
    {
        public static OrderReadModel Apply(OrderReadModel? current, EventEnvelope<OrderEvent> envelope)
        {
            ArgumentNullException.ThrowIfNull(envelope);

            var readModel = current ?? new OrderReadModel
            {
                OrderId = envelope.Data.OrderId.ToString("D"),
            };

            switch (envelope.Data)
            {
                case OrderCreated created:
                    readModel.OrderId = created.OrderId.ToString("D");
                    readModel.CustomerId = created.CustomerId;
                    readModel.Currency = created.Currency;
                    readModel.Status = "Draft";
                    break;
                case OrderItemAdded added:
                    readModel.ItemCount += added.Quantity;
                    readModel.TotalAmount += added.UnitPrice * added.Quantity;
                    break;
                case OrderItemRemoved removed:
                    readModel.ItemCount = Math.Max(0, readModel.ItemCount - removed.Quantity);
                    readModel.TotalAmount = Math.Max(0m, readModel.TotalAmount - (removed.UnitPrice * removed.Quantity));
                    break;
                case OrderConfirmed:
                    readModel.Status = "Confirmed";
                    break;
                case OrderCancelled:
                    readModel.Status = "Cancelled";
                    break;
            }

            readModel.Version = envelope.Version;
            readModel.UpdatedAt = envelope.CreatedAt;
            return readModel;
        }
    }
}
