using System;
using System.Linq;
using System.Linq.Expressions;

namespace MS.Microservice.Core.Specification;

internal static class ExpressionCombiner
{
    public static Expression<Func<T, bool>> AndAlso<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        => left.And(right);

    public static Expression<Func<T, bool>> OrElse<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        => left.Or(right);
}
