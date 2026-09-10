using FluentAssertions;
using MS.Microservice.Core.Specification;
using MS.Microservice.Persistence.EFCore.DbContext;

namespace MS.Microservice.Persistence.EFCore.Tests;

public sealed class EfCoreQueryableExtensionsTests
{
    [Fact]
    public void ApplySpecification_WhenCriteriaOrderingAndPagingAreDefined_ShouldApplyAllQueryableOperations()
    {
        var result = CreateOrders()
            .AsQueryable()
            .ApplySpecification(new DescendingPagedOrderSpecification())
            .Select(order => order.Id)
            .ToList();

        result.Should().Equal(3, 1);
    }

    [Fact]
    public void ApplySpecification_WhenEvaluateCriteriaOnlyIsTrue_ShouldSkipOrderingAndPaging()
    {
        var result = CreateOrders()
            .AsQueryable()
            .ApplySpecification(new DescendingPagedOrderSpecification(), evaluateCriteriaOnly: true)
            .Select(order => order.Id)
            .ToList();

        result.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void ApplySpecification_WhenProjectionSelectorExists_ShouldProjectFromAppliedQuery()
    {
        var result = CreateOrders()
            .AsQueryable()
            .ApplySpecification(new ProjectedOrderSpecification())
            .ToList();

        result.Should().Equal("order-3", "order-1");
    }

    [Fact]
    public void ApplySpecification_WhenProjectionSelectorIsMissing_ShouldThrowInvalidOperationException()
    {
        Action action = () => CreateOrders()
            .AsQueryable()
            .ApplySpecification(new MissingProjectionSpecification())
            .ToList();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*requires a Selector*");
    }

    [Fact]
    public void ApplySpecification_WhenAscendingAndThenDescendingOrderAreDefined_ShouldApplyChainedOrdering()
    {
        var result = CreateOrders()
            .AsQueryable()
            .ApplySpecification(new AscendingThenDescendingOrderSpecification())
            .Select(order => order.Id)
            .ToList();

        result.Should().Equal(1, 2, 3, 4);
    }

    private static List<TestOrder> CreateOrders()
    {
        return
        [
            new TestOrder { Id = 1, Category = "A", Total = 20m, Description = "order-1" },
            new TestOrder { Id = 2, Category = "A", Total = 10m, Description = "order-2" },
            new TestOrder { Id = 3, Category = "B", Total = 30m, Description = "order-3" },
            new TestOrder { Id = 4, Category = "B", Total = 5m, Description = "order-4" },
        ];
    }

    private sealed class DescendingPagedOrderSpecification : Specification<TestOrder>
    {
        public DescendingPagedOrderSpecification()
        {
            Where(order => order.Total >= 10m);
            OrderByDescending(order => order.Total);
            ThenBy(order => order.Id);
            ApplyPaging(0, 2);
        }
    }

    private sealed class AscendingThenDescendingOrderSpecification : Specification<TestOrder>
    {
        public AscendingThenDescendingOrderSpecification()
        {
            Where(order => order.Total > 0m);
            OrderBy(order => order.Category);
            ThenByDescending(order => order.Total);
        }
    }

    private sealed class ProjectedOrderSpecification : Specification<TestOrder, string>
    {
        public ProjectedOrderSpecification()
        {
            Where(order => order.Total >= 10m);
            OrderByDescending(order => order.Total);
            ThenBy(order => order.Id);
            ApplyPaging(0, 2);
            Select(order => order.Description);
        }
    }

    private sealed class MissingProjectionSpecification : Specification<TestOrder, string>
    {
        public MissingProjectionSpecification()
        {
            Where(order => order.Total >= 10m);
        }
    }

    private sealed class TestOrder
    {
        public int Id { get; init; }
        public string Category { get; init; } = string.Empty;
        public decimal Total { get; init; }
        public string Description { get; init; } = string.Empty;
    }
}
