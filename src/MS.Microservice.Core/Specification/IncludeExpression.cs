using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace MS.Microservice.Core.Specification;

/// <summary>
/// 单个导航属性 Include
/// </summary>
internal sealed class SingleIncludeExpression<TEntity, TProperty>(Expression<Func<TEntity, TProperty>> expression) : IIncludeExpression
{
    private readonly Expression<Func<TEntity, TProperty>> _expression = expression;

    public string ExpressionString { get; } = expression.ToString();

    public TQuery Accept<TQuery, TEntity1>(IIncludeExpressionVisitor<TQuery, TEntity1> visitor, TQuery query)
    {
        if (typeof(TEntity1) != typeof(TEntity))
            throw new InvalidOperationException($"Entity type mismatch: expected {typeof(TEntity).Name}, got {typeof(TEntity1).Name}");

        // 强制转换访问者以匹配泛型类型
        var typedVisitor = (IIncludeExpressionVisitor<TQuery, TEntity>)visitor;
        return typedVisitor.VisitSingleInclude(query, _expression);
    }
}

/// <summary>
/// List<T> 集合导航属性 Include
/// </summary>
internal sealed class ListIncludeExpression<TEntity, TProperty>(Expression<Func<TEntity, List<TProperty>>> expression) : IIncludeExpression
{
    private readonly Expression<Func<TEntity, List<TProperty>>> _expression = expression;

    public string ExpressionString { get; } = expression.ToString();

    public TQuery Accept<TQuery, TEntity1>(IIncludeExpressionVisitor<TQuery, TEntity1> visitor, TQuery query)
    {
        if (typeof(TEntity1) != typeof(TEntity))
            throw new InvalidOperationException($"Entity type mismatch: expected {typeof(TEntity).Name}, got {typeof(TEntity1).Name}");

        var typedVisitor = (IIncludeExpressionVisitor<TQuery, TEntity>)visitor;
        return typedVisitor.VisitCollectionInclude(query, _expression);
    }
}

/// <summary>
/// ICollection<T> 集合导航属性 Include
/// </summary>
internal sealed class ICollectionIncludeExpression<TEntity, TProperty>(Expression<Func<TEntity, ICollection<TProperty>>> expression) : IIncludeExpression
{
    private readonly Expression<Func<TEntity, ICollection<TProperty>>> _expression = expression;

    public string ExpressionString { get; } = expression.ToString();

    public TQuery Accept<TQuery, TEntity1>(IIncludeExpressionVisitor<TQuery, TEntity1> visitor, TQuery query)
    {
        if (typeof(TEntity1) != typeof(TEntity))
            throw new InvalidOperationException($"Entity type mismatch: expected {typeof(TEntity).Name}, got {typeof(TEntity1).Name}");

        var typedVisitor = (IIncludeExpressionVisitor<TQuery, TEntity>)visitor;
        return typedVisitor.VisitICollectionInclude(query, _expression);
    }
}