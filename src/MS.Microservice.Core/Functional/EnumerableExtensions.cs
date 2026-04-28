namespace MS.Microservice.Core.Functional
{
    public static class EnumerableExtensions
    {
        public static IEnumerable<R> Map<T, R>(this IEnumerable<T> list, Func<T, R> func)
          => list.Select(func);
    }
}
