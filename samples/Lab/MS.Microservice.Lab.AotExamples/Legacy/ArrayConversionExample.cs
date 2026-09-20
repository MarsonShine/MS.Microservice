namespace MS.Microservice.Lab.AotExamples.Legacy;

/// <summary>保留原转换流程：完整物化后再次枚举输入。</summary>
public static class ArrayConversionExample
{
    public static TResult[] ToArray<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, TResult> conveter)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (conveter == null) throw new ArgumentNullException(nameof(conveter));
        return ConvertInterator(source, conveter);
    }

    private static TResult[] ConvertInterator<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, TResult> conveter)
    {
        var sourceArray = source.ToArray();
        TResult[] results = new TResult[sourceArray.Length];
        ForEachInterator(source, (item, index) =>
        {
            results[index] = conveter(item);
        });
        return results;
    }

    private static void ForEachInterator<TSource>(IEnumerable<TSource> source, Action<TSource, int> action)
    {
        var index = -1;
        foreach (TSource item in source)
        {
            index = checked(index + 1);
            action(item, index);
        }
    }
}
