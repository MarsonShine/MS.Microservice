using System;
using System.Linq.Expressions;

namespace MS.Microservice.Core.Specification;

public enum OrderType
{
    OrderBy,
    OrderByDescending,
    ThenBy,
    ThenByDescending
}

public sealed class OrderExpression<T>
{
    public required Expression<Func<T, object>> KeySelector { get; init; }
    public required OrderType OrderType { get; init; }
}