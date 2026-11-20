using System.Linq.Expressions;
using MS.Microservice.Core.Memory;
using MS.Microservice.Core.Specification;

internal static class QueryCases
{
    public static void Run()
    {
        Expression<Func<int, bool>> lowerBound = value => value >= 0;
        Expression<Func<int, bool>> upperBound = value => value < 100;
        Bench.Measure("specification: eight predicates", 128,
            () => new RangeSpec(lowerBound, upperBound),
            spec =>
            {
                var matches = spec.Criteria!.Compile();
                return matches(50) && !matches(-1) && !matches(100);
            });

        var cache = new WeakEvictionCache<int, object>(TimeSpan.FromHours(1));
        var value = new object();
        for (int i = 0; i < 1024; i++) cache.Add(i, value);
        Bench.Measure("weak cache: scan 1024 live entries", 128,
            () =>
            {
                cache.DoWeakEviction();
                return cache;
            },
            scanned => scanned.TryGet(1023, out var found) && ReferenceEquals(found, value));
    }

    private sealed class RangeSpec : Specification<int>
    {
        public RangeSpec(Expression<Func<int, bool>> lowerBound, Expression<Func<int, bool>> upperBound)
        {
            for (int i = 0; i < 4; i++)
            {
                Where(lowerBound);
                Where(upperBound);
            }
        }
    }
}
