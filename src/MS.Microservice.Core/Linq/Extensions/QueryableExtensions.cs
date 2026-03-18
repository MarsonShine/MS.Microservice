using System.Linq.Expressions;

namespace System.Linq
{
    public static partial class QueryableExtensions
    {
        extension<T>(IQueryable<T> query)
        {
            public IQueryable<T> WhereIf(bool condition, Expression<Func<T, bool>> predicate)
                => condition ? query.Where(predicate) : query;
        }
    }
}
