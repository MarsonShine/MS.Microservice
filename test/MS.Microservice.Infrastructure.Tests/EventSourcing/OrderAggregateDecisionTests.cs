using System;
using FluentAssertions;
using MS.Microservice.Domain.Aggregates.OrderAggregate;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.EventSourcing;

public sealed class OrderAggregateDecisionTests
{
    [Fact]
    public void Decide_WhenCreateOrderIsInvalid_ShouldReturnValidation()
    {
        var decision = OrderAggregate.Decide(OrderState.Initial, new CreateOrder(Guid.Empty, "", ""));

        decision.IsLeft.Should().BeTrue();
        decision.Left.Code.Should().Be("validation");
        decision.Left.Details.Should().HaveCount(3);
    }

    [Fact]
    public void Decide_WhenAddItemBeforeCreate_ShouldReturnValidation()
    {
        var orderId = Guid.NewGuid();

        var decision = OrderAggregate.Decide(OrderState.Initial, new AddOrderItem(orderId, "sku-1", 10m, 1));

        decision.IsLeft.Should().BeTrue();
        decision.Left.Code.Should().Be("validation");
    }

    [Fact]
    public void Decide_WhenAddItemCommandIsInvalid_ShouldReturnValidation()
    {
        var orderId = Guid.NewGuid();
        var state = OrderAggregate.Fold([new OrderCreated(orderId, "cust-001", "CNY")]);

        var decision = OrderAggregate.Decide(state, new AddOrderItem(orderId, "", 0, 0));

        decision.IsLeft.Should().BeTrue();
        decision.Left.Code.Should().Be("validation");
        decision.Left.Details.Should().HaveCount(3);
    }

    [Fact]
    public void Decide_WhenRemoveItemThatDoesNotExist_ShouldReturnValidation()
    {
        var orderId = Guid.NewGuid();
        var state = OrderAggregate.Fold([new OrderCreated(orderId, "cust-001", "CNY")]);

        var decision = OrderAggregate.Decide(state, new RemoveOrderItem(orderId, "sku-1", 1));

        decision.IsLeft.Should().BeTrue();
        decision.Left.Message.Should().Be("待移除的商品不存在于订单中。");
    }

    [Fact]
    public void Decide_WhenRemoveQuantityTooLarge_ShouldReturnValidation()
    {
        var orderId = Guid.NewGuid();
        var state = OrderAggregate.Fold([
            new OrderCreated(orderId, "cust-001", "CNY"),
            new OrderItemAdded(orderId, "sku-1", 10m, 1)
        ]);

        var decision = OrderAggregate.Decide(state, new RemoveOrderItem(orderId, "sku-1", 2));

        decision.IsLeft.Should().BeTrue();
        decision.Left.Message.Should().Be("移除商品数量不能超过当前订单中的数量。");
    }

    [Fact]
    public void Decide_WhenCancelWithoutReason_ShouldReturnValidation()
    {
        var orderId = Guid.NewGuid();
        var state = OrderAggregate.Fold([new OrderCreated(orderId, "cust-001", "CNY")]);

        var decision = OrderAggregate.Decide(state, new CancelOrder(orderId, ""));

        decision.IsLeft.Should().BeTrue();
        decision.Left.Message.Should().Be("取消订单时必须提供原因。");
    }

    [Fact]
    public void Decide_WhenCreateOrderAlreadyExists_ShouldReturnConflict()
    {
        var orderId = Guid.NewGuid();
        var state = OrderAggregate.Fold([new OrderCreated(orderId, "cust-001", "CNY")]);

        var decision = OrderAggregate.Decide(state, new CreateOrder(orderId, "cust-002", "USD"));

        decision.IsLeft.Should().BeTrue();
        decision.Left.Code.Should().Be("conflict");
    }

    [Fact]
    public void Decide_WhenRemoveQuantityIsNonPositive_ShouldReturnValidation()
    {
        var orderId = Guid.NewGuid();
        var state = OrderAggregate.Fold([
            new OrderCreated(orderId, "cust-001", "CNY"),
            new OrderItemAdded(orderId, "sku-1", 10m, 1)
        ]);

        var decision = OrderAggregate.Decide(state, new RemoveOrderItem(orderId, "sku-1", 0));

        decision.IsLeft.Should().BeTrue();
        decision.Left.Code.Should().Be("validation");
    }

    [Fact]
    public void Decide_WhenOrderAlreadyConfirmed_ShouldReturnConflict()
    {
        var orderId = Guid.NewGuid();
        var state = OrderAggregate.Fold([
            new OrderCreated(orderId, "cust-001", "CNY"),
            new OrderItemAdded(orderId, "sku-1", 10m, 1),
            new OrderConfirmed(orderId)
        ]);

        var decision = OrderAggregate.Decide(state, new AddOrderItem(orderId, "sku-2", 20m, 1));

        decision.IsLeft.Should().BeTrue();
        decision.Left.Code.Should().Be("conflict");
    }

    [Fact]
    public void Decide_WhenOrderAlreadyCancelled_ShouldReturnConflict()
    {
        var orderId = Guid.NewGuid();
        var state = OrderAggregate.Fold([
            new OrderCreated(orderId, "cust-001", "CNY"),
            new OrderCancelled(orderId, "customer requested")
        ]);

        var decision = OrderAggregate.Decide(state, new AddOrderItem(orderId, "sku-2", 20m, 1));

        decision.IsLeft.Should().BeTrue();
        decision.Left.Code.Should().Be("conflict");
    }
}
