using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace MS.Microservice.Core.Specification;

/// <summary>
/// Include 表达式访问者接口
/// </summary>
/// <typeparam name="TQuery">查询类型（如 ISugarQueryable<T>）</typeparam>
/// <typeparam name="TEntity">实体类型</typeparam>
public interface IIncludeExpressionVisitor<TQuery, TEntity>
{
    /// <summary>
    /// 访问单个导航属性 Include
    /// </summary>
    TQuery VisitSingleInclude<TProperty>(TQuery query, Expression<Func<TEntity, TProperty>> expression);

    /// <summary>
    /// 访问集合导航属性 Include
    /// </summary>
    TQuery VisitCollectionInclude<TProperty>(TQuery query, Expression<Func<TEntity, List<TProperty>>> expression);

    /// <summary>
    /// 访问 ICollection 导航属性 Include
    /// </summary>
    TQuery VisitICollectionInclude<TProperty>(TQuery query, Expression<Func<TEntity, ICollection<TProperty>>> expression);
}