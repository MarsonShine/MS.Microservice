namespace MS.Microservice.Lab.AotExamples.Static.Query;

/// <summary>字段顺序和读取方式在编译期声明，不生成运行时代码。</summary>
public sealed class QueryMappingExample<T>(params (string Name, Func<T, object?> Read)[] fields)
{
    private readonly (string Name, Func<T, object?> Read)[] fields = [.. fields];

    public string Format(T value)
    {
        var parts = new List<string>();
        foreach (var (name, read) in fields) QueryExampleValues.Append(name, read(value), parts);
        return string.Join('&', parts);
    }
}
