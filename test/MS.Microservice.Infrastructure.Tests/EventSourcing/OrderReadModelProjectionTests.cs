using FluentAssertions;
using MS.Microservice.Domain.Aggregates.OrderAggregate;
using MS.Microservice.Domain.EventSourcing;
using MS.Microservice.Infrastructure.EventSourcing;
using MS.Microservice.Infrastructure.EventSourcing.Orders;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.EventSourcing
{
    public class OrderReadModelProjectionTests
    {
        [Fact]
        public void Apply_WhenReplayEvents_ShouldUpdateReadModel()
        {
            var orderId = Guid.NewGuid();
            OrderReadModel? readModel = null;

            readModel = OrderReadModelProjection.Apply(readModel, Envelope(new OrderCreated(orderId, "cust-001", "CNY"), 1, 1));
            readModel = OrderReadModelProjection.Apply(readModel, Envelope(new OrderItemAdded(orderId, "sku-1", 10m, 2), 2, 2));
            readModel = OrderReadModelProjection.Apply(readModel, Envelope(new OrderConfirmed(orderId), 3, 3));

            readModel.Should().NotBeNull();
            readModel!.OrderId.Should().Be(orderId.ToString("D"));
            readModel.CustomerId.Should().Be("cust-001");
            readModel.TotalAmount.Should().Be(20m);
            readModel.ItemCount.Should().Be(2);
            readModel.Status.Should().Be("Confirmed");
            readModel.Version.Should().Be(3);
        }

        [Fact]
        public void Apply_WhenRemoveItem_ShouldKeepProjectionIdempotentForCounts()
        {
            var orderId = Guid.NewGuid();
            var readModel = new OrderReadModel
            {
                OrderId = orderId.ToString("D"),
                CustomerId = "cust-001",
                Currency = "CNY",
                ItemCount = 2,
                TotalAmount = 20m,
                Status = "Draft",
            };

            var projected = OrderReadModelProjection.Apply(readModel, Envelope(new OrderItemRemoved(orderId, "sku-1", 10m, 5), 4, 4));

            projected.ItemCount.Should().Be(0);
            projected.TotalAmount.Should().Be(0m);
            projected.Version.Should().Be(4);
        }

        private static EventEnvelope<OrderEvent> Envelope(OrderEvent @event, int version, long globalPosition)
            => new(
                Guid.NewGuid(),
                OrderAggregate.GetStreamId(@event.OrderId),
                OrderAggregate.StreamType,
                version,
                globalPosition,
                @event,
                new EventMetadata(),
                DateTimeOffset.UtcNow);
    }
}
