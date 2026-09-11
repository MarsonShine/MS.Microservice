using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using MS.Microservice.Core.Reflection;

namespace MS.Microservice.Core.Net.Http;

/// <summary>把查询对象按属性写入 <c>name=value</c> 参数集合，元数据只解释一次。</summary>
/// <remarks>
/// 属性读取委托由 <see cref="PropertyAccessors" /> 按类型编译并缓存，热路径上不再有属性枚举、
/// LINQ 管线与 <c>PropertyInfo.GetValue</c> 装箱。值语义：<c>null</c> 跳过；
/// <see cref="DateTime" /> / <see cref="DateTimeOffset" /> 用往返格式，其余 <see cref="IFormattable" />
/// 走不变文化；非 <see cref="string" /> 的 <see cref="System.Collections.IEnumerable" /> 展开为重复参数名。
/// </remarks>
public static class QueryStringParameters
{
    private static readonly ConcurrentDictionary<Type, Func<object, ICollection<string>, int>> Populators = new();

    /// <summary>把查询对象追加到 <paramref name="destination" />，返回追加后的元素个数。</summary>
    public static int Dispatch(object? body, ICollection<string> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        switch (body)
        {
            case null:
                return destination.Count;
            case IDictionary dictionary:
                return AppendDictionary(dictionary, destination);
            case string text:
                // 刻意不解释字符串体：它既不是查询对象也不是字典，静默展成空参数会掩盖调用错误。
                return destination.Count;
            default:
                return Populator(body.GetType())(body, destination);
        }
    }

    private static Func<object, ICollection<string>, int> Populator(Type type)
    {
        if (Populators.TryGetValue(type, out var populator)) return populator;
        return Populators.GetOrAdd(type, PropertyAccessors.Materializer(type));
    }

    private static int AppendDictionary(IDictionary dictionary, ICollection<string> destination)
    {
        // Dictionary<string, T> 与 Dictionary<string, object?> 是同一泛型实例，值类型字典会落到下面的分支。
        if (dictionary is Dictionary<string, object?> typed)
        {
            foreach (var (name, value) in typed) AppendValue(name, value, destination);
            return destination.Count;
        }

        foreach (DictionaryEntry entry in dictionary)
            AppendValue(Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "", entry.Value, destination);
        return destination.Count;
    }

    /// <summary>追加单个值；<c>null</c> 与字符串按单值处理，其余可枚举值展开为重复参数名。</summary>
    public static bool AppendValue(string name, object? value, ICollection<string> destination)
    {
        if (value is null) return false;
        if (value is IEnumerable values and not string) return AppendMany(name, values, destination);

        var text = value switch
        {
            DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
        destination.Add(string.Concat(Escape(name), "=", Escape(text ?? "")));
        return true;
    }

    /// <summary>把可枚举值在同一参数名下展开。</summary>
    public static bool AppendMany(string name, IEnumerable values, ICollection<string> destination)
    {
        var appended = false;
        foreach (var value in values) appended |= AppendValue(name, value, destination);
        return appended;
    }

    /// <summary>可枚举属性的专用入口：值为 <c>null</c> 时按"无参数"跳过。</summary>
    /// <remarks>
    /// 两种编译实现都直接调用本方法，而不是先判空再调 <see cref="AppendMany" />：
    /// 编译产物里多一个判空分支就要在 IL 与表达树两边各写一遍，放在这里只有一处。
    /// </remarks>
    public static bool AppendManyIfNotNull(string name, IEnumerable? values, ICollection<string> destination)
        => values is not null && AppendMany(name, values, destination);

    /// <summary>只在出现保留字符时调用 <see cref="Uri.EscapeDataString" />，跳过绝大多数参数名与数字值的分配。</summary>
    private static string Escape(string text)
    {
        foreach (var character in text)
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '.' or '_' or '~'))
                return Uri.EscapeDataString(text);
        return text;
    }
}
