namespace MS.Microservice.Lab.AotExamples.Static;

/// <summary>通过官方 LINQ 投影并物化，只枚举输入一次。</summary>
public static class ArrayConversionExample
{
    public static TResult[] ToArray<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, TResult> conveter)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (conveter == null) throw new ArgumentNullException(nameof(conveter));
        return source.Select(conveter).ToArray();
    }
}
