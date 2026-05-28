namespace MS.Microservice.Core.Functional;

public static class EnumerableExtensions
{
    public static IEnumerable<R> Map<T, R>(this IEnumerable<T> list, Func<T, R> func)
      => list.Select(func);

    public static Func<IEnumerable<T>,T,IEnumerable<T>> Append<T>() => (list, item) => list.Append(item);

    extension<T>(IEnumerable<T> ts)
    {
        public Option<IEnumerable<R>> Traverse<R>(Func<T, Option<R>> f) => ts.Aggregate(
                seed: (Option<IEnumerable<R>>)F.Some(Enumerable.Empty<R>()),
                func: (optRs, t) => from rs in optRs
                                    from r in f(t)
                                    select rs.Append(r));

        public Validation<IEnumerable<R>> TraverseM<R>(Func<T, Validation<R>> f) => ts.Aggregate(
                seed: F.Valid(Enumerable.Empty<R>()),
                func: (valRs, t) => from rs in valRs
                                    from r in f(t)
                                    select rs.Append(r));

        public Validation<IEnumerable<R>> TraverseA<R>(Func<T,Validation<R>> f) => ts.Aggregate(
                seed: F.Valid(Enumerable.Empty<R>()),
                func: (valRs, t) => F.Valid(Append<R>())
                                        .Apply(valRs)
                                        .Apply(f(t)));

        public Validation<IEnumerable<R>> Traverse<R>(Func<T, Validation<R>> f) => TraverseA(ts, f);
    }
}
