using SqlSugar;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace MS.Microservice.Infrastructure.SqlSugar
{
    public static class SqlSugarExtensions
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
    }
}
