using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace MS.Microservice.Core.Specification;

public interface ISpecification<T>
{
    Expression<Func<T, bool>>? Criteria { get; }
    IReadOnlyList<IIncludeExpression> Includes { get; }
    IReadOnlyList<OrderExpression<T>> OrderExpressions { get; }

    int? Take { get; }
    int? Skip { get; }
    bool IsPagingEnabled { get; }
    bool IgnoreQueryFilters { get; }
}

public interface ISpecification<T, TResult> : ISpecification<T>
{
    Expression<Func<T, TResult>>? Selector { get; }
}