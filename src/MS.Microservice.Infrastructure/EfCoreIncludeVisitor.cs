using Microsoft.EntityFrameworkCore;
using MS.Microservice.Core.Specification;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace MS.Microservice.Infrastructure;

/// <summary>
/// EF Core Include 访问者实现 - 无反射，高性能
/// </summary>
internal class EfCoreIncludeVisitor<TEntity> : IIncludeExpressionVisitor<IQueryable<TEntity>, TEntity>
    where TEntity : class
{
    public IQueryable<TEntity> VisitSingleInclude<TProperty>(
        IQueryable<TEntity> query,
        Expression<Func<TEntity, TProperty>> expression)
    {
        return query.Include(expression);
    }

    public IQueryable<TEntity> VisitCollectionInclude<TProperty>(
        IQueryable<TEntity> query,
        Expression<Func<TEntity, List<TProperty>>> expression)
    {
        return query.Include(expression);
    }

    public IQueryable<TEntity> VisitICollectionInclude<TProperty>(
        IQueryable<TEntity> query,
        Expression<Func<TEntity, ICollection<TProperty>>> expression)
    {
        return query.Include(expression);
    }
}