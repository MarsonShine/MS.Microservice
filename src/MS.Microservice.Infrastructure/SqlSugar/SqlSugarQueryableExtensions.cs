using MS.Microservice.Core.Specification;
using MS.Microservice.Infrastructure.SqlSugar.Specification;
using SqlSugar;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace MS.Microservice.Infrastructure.SqlSugar
{
    public static partial class SqlSugarQueryableExtensions
    {
        extension<T>(ISugarQueryable<T> queryable)
        {
            public ISugarQueryable<T> Includes<TReturn>(Expression<Func<T, List<TReturn>?>> include, bool isInclude = true)
            {
                if (isInclude)
                    queryable.Includes(include);
                return queryable;
            }

            public ISugarQueryable<T> ClearFilterIF(bool isIgnoreFilter)
            {
                if (isIgnoreFilter)
                    return queryable.ClearFilter();
                return queryable;
            }
        }

        extension<T>(ISugarQueryable<T> query) where T : class, new()
        {
            /// <summary>
            /// 应用 Specification 到 SqlSugar 查询
            /// </summary>
            public ISugarQueryable<T> ApplySpecification(ISpecification<T> spec, bool evaluateCriteriaOnly = false)
            {
                if (spec.Criteria is not null)
                    query = query.Where(spec.Criteria);

                if (evaluateCriteriaOnly)
                    return query;

                var includeVisitor = new SqlSugarIncludeVisitor<T>();
                foreach (var include in spec.Includes)
                {
                    query = include.Accept(includeVisitor, query);
                }

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

                if (spec.IsPagingEnabled)
                {
                    if (spec.Skip.HasValue) query = query.Skip(spec.Skip.Value);
                    if (spec.Take.HasValue) query = query.Take(spec.Take.Value);
                }

                return query;
            }
        }

        extension<T, TResult>(ISugarQueryable<T> query) where T : class, new()
        {
            /// <summary>
            /// 应用投影 Specification 到 SqlSugar 查询
            /// </summary>
            public ISugarQueryable<TResult> ApplySpecification(ISpecification<T, TResult> spec)
            {
                var appliedQuery = query.ApplySpecification(spec, evaluateCriteriaOnly: false);

                if (spec.Selector is null)
                    throw new InvalidOperationException("Projection specification requires a Selector.");

                return appliedQuery.Select(spec.Selector);
            }
        }
    }
}
