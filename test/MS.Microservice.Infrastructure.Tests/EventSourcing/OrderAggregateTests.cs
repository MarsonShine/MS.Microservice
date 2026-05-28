using FluentAssertions;
using MS.Microservice.Domain.Aggregates.OrderAggregate;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.EventSourcing
{
    public class OrderAggregateTests
    {
        [Fact]
        public void Decide_WhenCreateOrderIsValid_ShouldProduceOrderCreatedEvent()
        {
            var orderId = Guid.NewGuid();

            var decision = OrderAggregate.Decide(OrderState.Initial, new CreateOrder(orderId, "cust-001", "CNY"));

            decision.IsRight.Should().BeTrue();
            decision.Right.Should().ContainSingle();
            decision.Right[0].Should().BeOfType<OrderCreated>();
        }

        [Fact]
        public void Decide_WhenConfirmWithoutItems_ShouldReturnValidationError()
        {
            var orderId = Guid.NewGuid();
            var state = OrderAggregate.Fold([
                new OrderCreated(orderId, "cust-001", "CNY")
            ]);

            var decision = OrderAggregate.Decide(state, new ConfirmOrder(orderId));

            decision.IsLeft.Should().BeTrue();
            decision.Left.Code.Should().Be("validation");
        }

        [Fact]
        public void Fold_WhenReplayLifecycle_ShouldBuildCurrentState()
        {
            var orderId = Guid.NewGuid();
            var state = OrderAggregate.Fold([
                new OrderCreated(orderId, "cust-001", "CNY"),
                new OrderItemAdded(orderId, "sku-1", 10m, 2),
                new OrderItemAdded(orderId, "sku-2", 30m, 1),
                new OrderItemRemoved(orderId, "sku-1", 10m, 1),
                new OrderConfirmed(orderId)
            ]);

            state.Exists.Should().BeTrue();
            state.IsConfirmed.Should().BeTrue();
            state.TotalAmount.Should().Be(40m);
            state.Lines["sku-1"].Quantity.Should().Be(1);
            state.Version.Should().Be(5);
        }
    }
}
