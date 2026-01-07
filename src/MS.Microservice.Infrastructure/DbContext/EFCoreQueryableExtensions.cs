using Microsoft.EntityFrameworkCore;
using MS.Microservice.Core.Specification;
using NPOI.SS.Formula.Functions;
using System;
using System.Linq;

namespace MS.Microservice.Infrastructure.DbContext;

/// <summary>
/// EF Core 查询扩展方法
/// </summary>
public static partial class EfCoreQueryableExtensions
{
    extension<T>(IQueryable<T> query) where T : class
    {
        /// <summary>
        /// 应用 Specification 到 EF Core 查询
        /// </summary>
        public IQueryable<T> ApplySpecification(ISpecification<T> spec, bool evaluateCriteriaOnly = false)
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
    }

    extension<T, TResult>(IQueryable<T> query) where T : class
    {
        /// <summary>
        /// 应用投影 Specification 到 EF Core 查询
        /// </summary>
        public IQueryable<TResult> ApplySpecification(ISpecification<T, TResult> spec)
        {
            var appliedQuery = ApplySpecification(query, spec, evaluateCriteriaOnly: false);

            if (spec.Selector is null)
                throw new InvalidOperationException("Projection specification requires a Selector.");

            return appliedQuery.Select(spec.Selector);
        }
    }
}