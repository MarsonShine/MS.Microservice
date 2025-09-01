using MS.Microservice.Core.Specification;
using MS.Microservice.Infrastructure.SqlSugar.Specification;
using SqlSugar;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace MS.Microservice.Infrastructure.SqlSugar
{
    public static class SqlSugarQueryableExtensions
    {
        public static ISugarQueryable<T> Includes<T, TReturn>(this ISugarQueryable<T> queryable, Expression<Func<T, List<TReturn>?>> include, bool isInclude = true)
        {
            if (isInclude)
                queryable.Includes(include);
            return queryable;
        }

        public static ISugarQueryable<T> ClearFilterIF<T>(this ISugarQueryable<T> queryable, bool isIgnoreFilter)
        {
            if (isIgnoreFilter)
                return queryable.ClearFilter();
            return queryable;
        }

        /// <summary>
        /// 应用 Specification 到 SqlSugar 查询
        /// </summary>
        public static ISugarQueryable<T> ApplySpecification<T>(this ISugarQueryable<T> query, ISpecification<T> spec, bool evaluateCriteriaOnly = false)
            where T : class, new()
        {
            // Where 条件
            if (spec.Criteria is not null)
                query = query.Where(spec.Criteria);

            if (evaluateCriteriaOnly)
                return query;

            // Include - 使用访问者模式，无反射
            var includeVisitor = new SqlSugarIncludeVisitor<T>();
            foreach (var include in spec.Includes)
            {
                query = include.Accept(includeVisitor, query);
            }

            // 排序
            foreach (var order in spec.OrderExpressions)
            {
                var orderType = order.OrderType switch
                {
                    OrderType.OrderBy or OrderType.ThenBy => OrderByType.Asc,
                    OrderType.OrderByDescending or OrderType.ThenByDescending => OrderByType.Desc,
                    _ => OrderByType.Asc
                };
                query = query.OrderBy(order.KeySelector, orderType);
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
        /// 应用投影 Specification 到 SqlSugar 查询
        /// </summary>
        public static ISugarQueryable<TResult> ApplySpecification<T, TResult>(this ISugarQueryable<T> query, ISpecification<T, TResult> spec)
            where T : class, new()
        {
            var appliedQuery = query.ApplySpecification(spec, evaluateCriteriaOnly: false);

            if (spec.Selector is null)
                throw new InvalidOperationException("Projection specification requires a Selector.");

            return appliedQuery.Select(spec.Selector);
        }
    }
}
