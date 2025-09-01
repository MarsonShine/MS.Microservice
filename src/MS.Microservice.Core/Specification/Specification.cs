using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace MS.Microservice.Core.Specification;

public abstract class Specification<T> : ISpecification<T>
{
    private readonly List<IIncludeExpression> _includes = [];
    private readonly List<OrderExpression<T>> _orderExpressions = [];

    public Expression<Func<T, bool>>? Criteria { get; private set; }
    public IReadOnlyList<IIncludeExpression> Includes => _includes.AsReadOnly();
    public IReadOnlyList<OrderExpression<T>> OrderExpressions => _orderExpressions.AsReadOnly();

    public int? Take { get; private set; }
    public int? Skip { get; private set; }
    public bool IsPagingEnabled { get; private set; }

    public bool IgnoreQueryFilters { get; private set; }

    protected void Where(Expression<Func<T, bool>> criteria)
        => Criteria = Criteria is null ? criteria : ExpressionCombiner.AndAlso(Criteria, criteria);

    protected void OrWhere(Expression<Func<T, bool>> criteria)
        => Criteria = Criteria is null ? criteria : ExpressionCombiner.OrElse(Criteria, criteria);

    #region Includes
    /// <summary>
    /// 单个导航属性 Include
    /// </summary>
    protected void Include<TProperty>(Expression<Func<T, TProperty>> includeExpression)
        => _includes.Add(new SingleIncludeExpression<T, TProperty>(includeExpression));

    /// <summary>
    /// List<T> 集合导航属性 Include
    /// </summary>
    protected void IncludeList<TProperty>(Expression<Func<T, List<TProperty>>> includeExpression)
        => _includes.Add(new ListIncludeExpression<T, TProperty>(includeExpression));

    /// <summary>
    /// ICollection<T> 集合导航属性 Include
    /// </summary>
    protected void IncludeCollection<TProperty>(Expression<Func<T, ICollection<TProperty>>> includeExpression)
        => _includes.Add(new ICollectionIncludeExpression<T, TProperty>(includeExpression));
    #endregion

    protected void OrderBy(Expression<Func<T, object>> keySelector)
        => _orderExpressions.Add(new() { KeySelector = keySelector, OrderType = OrderType.OrderBy });
    protected void OrderByDescending(Expression<Func<T, object>> keySelector)
        => _orderExpressions.Add(new() { KeySelector = keySelector, OrderType = OrderType.OrderByDescending });
    protected void ThenBy(Expression<Func<T, object>> keySelector)
        => _orderExpressions.Add(new() { KeySelector = keySelector, OrderType = OrderType.ThenBy });
    protected void ThenByDescending(Expression<Func<T, object>> keySelector)
        => _orderExpressions.Add(new() { KeySelector = keySelector, OrderType = OrderType.ThenByDescending });

    protected void ApplyPaging(int skip, int take)
    {
        Skip = skip;
        Take = take;
        IsPagingEnabled = true;
    }

    protected void IgnoreGlobalQueryFilters(bool ignore = true) => IgnoreQueryFilters = ignore;
}

public abstract class Specification<T, TResult> : Specification<T>, ISpecification<T, TResult>
{
    public Expression<Func<T, TResult>>? Selector { get; private set; }

    protected void Select(Expression<Func<T, TResult>> selector) => Selector = selector;
}