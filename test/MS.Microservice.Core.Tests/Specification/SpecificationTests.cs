using MS.Microservice.Core.Specification;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace MS.Microservice.Core.Tests.Specification
{
    public class SpecificationTests
    {
        // Concrete specification for testing (since Specification<T> is abstract)
        private class TestProductSpec : Specification<Product>
        {
            public TestProductSpec() { }

            public void ApplyWhere(Expression<Func<Product, bool>> criteria) => Where(criteria);
            public void ApplyOrWhere(Expression<Func<Product, bool>> criteria) => OrWhere(criteria);
            public void ApplyOrderBy(Expression<Func<Product, object>> keySelector) => OrderBy(keySelector);
            public void ApplyOrderByDescending(Expression<Func<Product, object>> keySelector) => OrderByDescending(keySelector);
            public void ApplyThenBy(Expression<Func<Product, object>> keySelector) => ThenBy(keySelector);
            public void ApplyInclude(Expression<Func<Product, object>> include) => Include(include);
            public new void ApplyPaging(int skip, int take) => base.ApplyPaging(skip, take);
        }

        private class Product
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public string Category { get; set; } = string.Empty;
        }

        [Fact]
        public void Where_SingleCriteria_SetsCriteria()
        {
            var spec = new TestProductSpec();
            spec.ApplyWhere(p => p.Price > 100);

            Assert.NotNull(spec.Criteria);
            var compiled = spec.Criteria!.Compile();
            Assert.True(compiled(new Product { Price = 150 }));
            Assert.False(compiled(new Product { Price = 50 }));
        }

        [Fact]
        public void Where_MultipleCriteria_CombinesWithAnd()
        {
            var spec = new TestProductSpec();
            spec.ApplyWhere(p => p.Price > 100);
            spec.ApplyWhere(p => p.Name == "Widget");

            var compiled = spec.Criteria!.Compile();
            Assert.True(compiled(new Product { Price = 150, Name = "Widget" }));
            Assert.False(compiled(new Product { Price = 150, Name = "Other" }));
            Assert.False(compiled(new Product { Price = 50, Name = "Widget" }));
        }

        [Fact]
        public void OrWhere_CombinesWithOr()
        {
            var spec = new TestProductSpec();
            spec.ApplyWhere(p => p.Price > 100);
            spec.ApplyOrWhere(p => p.Name == "FreeItem");

            var compiled = spec.Criteria!.Compile();
            Assert.True(compiled(new Product { Price = 150, Name = "Other" }));
            Assert.True(compiled(new Product { Price = 0, Name = "FreeItem" }));
            Assert.False(compiled(new Product { Price = 50, Name = "Other" }));
        }

        [Fact]
        public void OrderBy_AddsOrderExpression()
        {
            var spec = new TestProductSpec();
            spec.ApplyOrderBy(p => p.Price);

            Assert.Single(spec.OrderExpressions);
            Assert.Equal(OrderType.OrderBy, spec.OrderExpressions[0].OrderType);
        }

        [Fact]
        public void OrderByDescending_AddsOrderExpression()
        {
            var spec = new TestProductSpec();
            spec.ApplyOrderByDescending(p => p.Price);

            Assert.Single(spec.OrderExpressions);
            Assert.Equal(OrderType.OrderByDescending, spec.OrderExpressions[0].OrderType);
        }

        [Fact]
        public void ThenBy_AddsToOrderExpressions()
        {
            var spec = new TestProductSpec();
            spec.ApplyOrderBy(p => p.Category);
            spec.ApplyThenBy(p => p.Name);

            Assert.Equal(2, spec.OrderExpressions.Count);
            Assert.Equal(OrderType.OrderBy, spec.OrderExpressions[0].OrderType);
            Assert.Equal(OrderType.ThenBy, spec.OrderExpressions[1].OrderType);
        }

        [Fact]
        public void Include_AddsIncludeExpression()
        {
            var spec = new TestProductSpec();
            spec.ApplyInclude(p => p.Category);

            Assert.Single(spec.Includes);
        }

        [Fact]
        public void ApplyPaging_SetsPagingProperties()
        {
            var spec = new TestProductSpec();
            spec.ApplyPaging(10, 20);

            Assert.True(spec.IsPagingEnabled);
            Assert.Equal(10, spec.Skip);
            Assert.Equal(20, spec.Take);
        }

        [Fact]
        public void NoPaging_ByDefault()
        {
            var spec = new TestProductSpec();

            Assert.False(spec.IsPagingEnabled);
            Assert.Null(spec.Skip);
            Assert.Null(spec.Take);
        }

        [Fact]
        public void Criteria_NullByDefault()
        {
            var spec = new TestProductSpec();
            Assert.Null(spec.Criteria);
        }
    }

    public class ExpressionCombinerTests
    {
        [Fact]
        public void AndAlso_CombinesCorrectly()
        {
            Expression<Func<int, bool>> left = x => x > 5;
            Expression<Func<int, bool>> right = x => x < 10;

            var combined = ExpressionCombiner.AndAlso(left, right);
            var func = combined.Compile();

            Assert.True(func(7));
            Assert.False(func(3));
            Assert.False(func(12));
        }

        [Fact]
        public void OrElse_CombinesCorrectly()
        {
            Expression<Func<int, bool>> left = x => x < 3;
            Expression<Func<int, bool>> right = x => x > 8;

            var combined = ExpressionCombiner.OrElse(left, right);
            var func = combined.Compile();

            Assert.True(func(1));
            Assert.True(func(10));
            Assert.False(func(5));
        }
    }
}
