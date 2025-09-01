using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace MS.Microservice.Core.Specification;

internal static class ExpressionCombiner
{
    public static Expression<T> Compose<T>(this Expression<T> first, Expression<T> second, Func<Expression, Expression, Expression> merge)
    {
        var map = first.Parameters
            .Select((f, i) => new { f, s = second.Parameters[i] })
            .ToDictionary(p => p.s, p => p.f);

        var secondBody = new ParameterReplacer(map).Visit(second.Body)!;
        return Expression.Lambda<T>(merge(first.Body, secondBody), first.Parameters);
    }

    public static Expression<Func<T, bool>> AndAlso<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        => left.Compose(right, Expression.AndAlso);

    public static Expression<Func<T, bool>> OrElse<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        => left.Compose(right, Expression.OrElse);

    private sealed class ParameterReplacer(Dictionary<ParameterExpression, ParameterExpression> map) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => map.TryGetValue(node, out var replacement) ? replacement : node;
    }
}