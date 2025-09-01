using Microsoft.EntityFrameworkCore;
using MS.Microservice.Core.Specification;
using System;
using System.Linq;

namespace MS.Microservice.Infrastructure.DbContext;

/// <summary>
/// EF Core 查询扩展方法
/// </summary>
public static class EfCoreQueryableExtensions
{
    /// <summary>
    /// 应用 Specification 到 EF Core 查询
    /// </summary>
    public static IQueryable<T> ApplySpecification<T>(this IQueryable<T> query, ISpecification<T> spec, bool evaluateCriteriaOnly = false)
        where T : class
    {
        // EF Core 特性应用
        if (spec.IgnoreQueryFilters)
            query = query.IgnoreQueryFilters();

        // Where 条件
        if (spec.Criteria is not null)
            query = query.Where(spec.Criteria);

        if (evaluateCriteriaOnly)
            return query;

        // Include - 使用访问者模式，无反射
        var includeVisitor = new EfCoreIncludeVisitor<T>();
        foreach (var include in spec.Includes)
        {
            query = include.Accept(includeVisitor, query);
        }

        // 排序
        IOrderedQueryable<T>? orderedQuery = null;
        foreach (var order in spec.OrderExpressions)
        {
            switch (order.OrderType)
            {
                case OrderType.OrderBy:
                    orderedQuery = query.OrderBy(order.KeySelector);
                    query = orderedQuery;
                    break;
                case OrderType.OrderByDescending:
                    orderedQuery = query.OrderByDescending(order.KeySelector);
                    query = orderedQuery;
                    break;
                case OrderType.ThenBy when orderedQuery is not null:
                    orderedQuery = orderedQuery.ThenBy(order.KeySelector);
                    query = orderedQuery;
                    break;
                case OrderType.ThenByDescending when orderedQuery is not null:
                    orderedQuery = orderedQuery.ThenByDescending(order.KeySelector);
                    query = orderedQuery;
                    break;
            }
        }

        // 分页
        if (spec.IsPagingEnabled)
        {
            if (spec.Skip.HasValue) query = query.Skip(spec.Skip.Value);
            if (spec.Take.HasValue) query = query.Take(spec.Take.Value);
        }

        return query;
    }

    /// <summary>
    /// 应用投影 Specification 到 EF Core 查询
    /// </summary>
    public static IQueryable<TResult> ApplySpecification<T, TResult>(this IQueryable<T> query, ISpecification<T, TResult> spec)
        where T : class
    {
        var appliedQuery = query.ApplySpecification(spec, evaluateCriteriaOnly: false);

        if (spec.Selector is null)
            throw new InvalidOperationException("Projection specification requires a Selector.");

        return appliedQuery.Select(spec.Selector);
    }
}