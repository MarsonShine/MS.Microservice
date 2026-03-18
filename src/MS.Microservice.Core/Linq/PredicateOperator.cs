using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace System.Linq
{
    /// <summary>
    /// https://github.com/scottksmith95/LINQKit/blob/master/src/LinqKit.Core/PredicateBuilder.cs
    /// </summary>
    public enum PredicateOperator
    {
        Or,
        And
    }

    public static partial class PredicateBuilder
    {
        private class RebindParameterVisitor : ExpressionVisitor
        {
            private readonly ParameterExpression _oldParameter;
            private readonly ParameterExpression _newParameter;
            public RebindParameterVisitor(ParameterExpression oldParameter, ParameterExpression newParameter)
            {
                _oldParameter = oldParameter;
                _newParameter = newParameter;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                return node == _oldParameter ? _newParameter : base.VisitParameter(node);
            }
        }

        public static ExpressionStarter<T> New<T>([AllowNull]Expression<Func<T, bool>> expr = null)
        {
            return new ExpressionStarter<T>(expr!);
        }

        /// <summary> Create an expression with a stub expression true or false to use when the expression is not yet started. </summary>
        public static ExpressionStarter<T> New<T>(bool defaultExpression)
        {
            return new ExpressionStarter<T>(defaultExpression);
        }

        extension<T>(Expression<Func<T, bool>> expr1)
        {
            public Expression<Func<T, bool>> Or(Expression<Func<T, bool>> expr2)
            {
                var expr2Body = new RebindParameterVisitor(expr2.Parameters[0], expr1.Parameters[0]).Visit(expr2.Body);
                return Expression.Lambda<Func<T, bool>>(Expression.OrElse(expr1.Body, expr2Body), expr1.Parameters);
            }

            public Expression<Func<T, bool>> And(Expression<Func<T, bool>> expr2)
            {
                var expr2Body = new RebindParameterVisitor(expr2.Parameters[0], expr1.Parameters[0]).Visit(expr2.Body);
                return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(expr1.Body, expr2Body), expr1.Parameters);
            }

            public Expression<Func<T, bool>> Not()
                => Expression.Lambda<Func<T, bool>>(Expression.Not(expr1.Body), expr1.Parameters);

            public Expression<Func<T, bool>> Extend(Expression<Func<T, bool>> second, PredicateOperator @operator = PredicateOperator.Or)
                => @operator == PredicateOperator.Or ? expr1.Or(second) : expr1.And(second);
        }

        extension<T>(ExpressionStarter<T> first)
        {
            public Expression<Func<T, bool>> Extend(Expression<Func<T, bool>> second, PredicateOperator @operator = PredicateOperator.Or)
                => @operator == PredicateOperator.Or ? first.Or(second) : first.And(second);
        }
    }
}
