namespace MS.Microservice.Core.Net.Http;

/// <summary>按调用方声明的顺序读取查询字段，不发现属性或编译表达式。</summary>
public sealed class QueryParameterMap<T>
{
    private readonly (string Name, Func<T, object?> Read)[] fields;

    public QueryParameterMap(params (string Name, Func<T, object?> Read)[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        foreach (var (name, read) in fields)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(read);
        }
        this.fields = [.. fields];
    }

    public int Append(T? value, ICollection<string> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (value is null) return destination.Count;
        foreach (var (name, read) in fields)
            QueryStringParameters.AppendValue(name, read(value), destination);
        return destination.Count;
    }
}
