namespace MS.Microservice.Lab.AotExamples.Static.Linq;

public static class MemoryFilter
{
    public static IEnumerable<T> Select<T>(IEnumerable<T> source, Func<T, bool> predicate)
        => source.Where(predicate);
}
