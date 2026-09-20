using System.Linq.Expressions;
using System.Reflection;

namespace MS.Microservice.Lab.AotExamples.Legacy.Query;

/// <summary>按类型缓存表达式访问器；完整旧 HTTP/IL 接入见同目录源码快照。</summary>
public static class QueryMappingExample<T>
{
    private static readonly (string Name, Func<T, object?> Read)[] Fields = typeof(T)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.GetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0)
        .Select(property =>
        {
            var parameter = Expression.Parameter(typeof(T));
            var read = Expression.Lambda<Func<T, object?>>(
                Expression.Convert(Expression.Property(parameter, property), typeof(object)), parameter).Compile();
            return (property.Name, read);
        }).ToArray();

    public static string Format(T value)
    {
        var parts = new List<string>();
        foreach (var (name, read) in Fields) QueryExampleValues.Append(name, read(value), parts);
        return string.Join('&', parts);
    }
}
