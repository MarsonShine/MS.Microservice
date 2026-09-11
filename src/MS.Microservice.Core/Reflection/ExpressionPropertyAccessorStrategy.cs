using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using MS.Microservice.Core.Net.Http;

namespace MS.Microservice.Core.Reflection;

/// <summary>用表达树编译的对照实现，语义与 <see cref="IlPropertyAccessorStrategy" /> 完全一致。</summary>
/// <remarks>
/// 与 IL 版的差别只有两处，都是"表达树拿不到静态类型"导致的：
/// 可枚举属性统一按 <c>IEnumerable</c> 分发（数组没有专用索引循环），枚举也走
/// <c>IFormattable.ToString(null, InvariantCulture)</c> 而不是 <c>Enum.ToString()</c>。
/// 好处是循环、装箱、异常都由框架生成，改动时不容易破坏栈平衡，可作为 IL 的错误对照面。
/// </remarks>
public sealed class ExpressionPropertyAccessorStrategy : PropertyAccessorStrategy
{
    public override Func<object, ICollection<string>, int> CompilePopulate(Type type)
    {
        var body = Expression.Parameter(typeof(object), "body");
        var typed = Expression.Variable(type, "typed");
        var destination = Expression.Parameter(typeof(ICollection<string>), "destination");
        var appended = Expression.Variable(typeof(bool), "appended");
        var expressions = new List<Expression> { Expression.Assign(typed, Expression.Convert(body, type)) };
        var any = false;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetMethod?.IsPublic != true) continue;
            if (property.GetIndexParameters().Length != 0) continue;

            var value = Expression.Property(typed, property);
            var expandable = IsExpandable(property.PropertyType);
            expressions.Add(Expression.Assign(appended, Expression.Call(
                AppendMethod(expandable),
                Expression.Constant(property.Name),
                Expression.Convert(value, expandable ? typeof(IEnumerable) : typeof(object)),
                destination)));
            any = true;
        }

        // 没有可写属性时仍然返回一个合法委托，调用方通过返回 0 判断无参数。
        if (!any) expressions.Add(Expression.Assign(appended, Expression.Constant(false)));

        expressions.Add(Expression.Property(destination, nameof(ICollection<string>.Count)));
        return Expression.Lambda<Func<object, ICollection<string>, int>>(
            Expression.Block([typed, appended], expressions), body, destination).Compile();
    }

    private static MethodInfo AppendMethod(bool enumerable)
        => typeof(QueryStringParameters).GetMethod(
            enumerable ? nameof(QueryStringParameters.AppendManyIfNotNull) : nameof(QueryStringParameters.AppendValue),
            [typeof(string), enumerable ? typeof(IEnumerable) : typeof(object), typeof(ICollection<string>)])!;

    public override Func<object, object?>? CompileGetter(PropertyInfo property)
    {
        if (property.GetMethod?.IsPublic != true) return null;
        var body = Expression.Parameter(typeof(object), "body");
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(Expression.Property(Expression.Convert(body, property.DeclaringType!), property), typeof(object)),
            body).Compile();
    }

    public override Action<object, object?>? CompileSetter(PropertyInfo property)
    {
        if (!IsWritable(property)) return null;
        var body = Expression.Parameter(typeof(object), "body");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Action<object, object?>>(
            Expression.Assign(
                Expression.Property(Expression.Convert(body, property.DeclaringType!), property),
                Expression.Convert(value, property.PropertyType)),
            body,
            value).Compile();
    }

    public override Func<object>? CompileFactory(Type type)
    {
        if (type.GetConstructor(Type.EmptyTypes) is null) return null;
        return Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(type), typeof(object))).Compile();
    }
}
