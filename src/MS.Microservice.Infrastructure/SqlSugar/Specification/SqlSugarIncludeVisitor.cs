using MS.Microservice.Core.Specification;
using SqlSugar;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace MS.Microservice.Infrastructure.SqlSugar.Specification;

/// <summary>
/// SqlSugar Include 访问者实现
/// </summary>
internal class SqlSugarIncludeVisitor<TEntity> : IIncludeExpressionVisitor<ISugarQueryable<TEntity>, TEntity>
    where TEntity : class, new()
{
    public ISugarQueryable<TEntity> VisitSingleInclude<TProperty>(
        ISugarQueryable<TEntity> query,
        Expression<Func<TEntity, TProperty>> expression)
    {
        return query.Includes(expression);
    }

    public ISugarQueryable<TEntity> VisitCollectionInclude<TProperty>(
        ISugarQueryable<TEntity> query,
        Expression<Func<TEntity, List<TProperty>>> expression)
    {
        return query.Includes(expression);
    }

    public ISugarQueryable<TEntity> VisitICollectionInclude<TProperty>(
        ISugarQueryable<TEntity> query,
        Expression<Func<TEntity, ICollection<TProperty>>> expression)
    {
        return query.Includes(expression);
    }
}