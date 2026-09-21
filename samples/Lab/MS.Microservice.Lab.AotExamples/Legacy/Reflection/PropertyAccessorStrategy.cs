using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MS.Microservice.Lab.AotExamples.Legacy.Reflection;

/// <summary>两种编译实现共享的类型过滤与顺序契约。</summary>
public abstract class PropertyAccessorStrategy : IPropertyAccessorStrategy
{
    public abstract Func<object, ICollection<string>, int> CompilePopulate(Type type);
    public abstract Func<object, object?>? CompileGetter(PropertyInfo property);
    public abstract Action<object, object?>? CompileSetter(PropertyInfo property);
    public abstract Func<object>? CompileFactory(Type type);

    /// <summary>把类型体对象的属性依次写入目标集合，返回写入后的参数个数。</summary>
    public virtual PropertyAccessor[] CreateAccessors(Type type)
    {
        var result = new List<PropertyAccessor>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetMethod?.IsPublic != true) continue;
            if (property.GetIndexParameters().Length != 0) continue;

            result.Add(new PropertyAccessor(
                property,
                property.Name,
                property.PropertyType,
                CompileGetter(property),
                CompileSetter(property)));
        }

        return [.. result];
    }

    /// <summary>属性存储类型是否可按 <see cref="IEnumerable" /> 展开；字符串按单值处理，与原语义一致。</summary>
    protected static bool IsExpandable(Type propertyType)
        => propertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(propertyType);

    /// <summary>属性是否可写。<c>init</c> 访问器只能在构造函数里赋值：反射能调用，编译产物不应生成写入。</summary>
    protected static bool IsWritable(PropertyInfo property)
        => property.SetMethod?.IsPublic == true
            && !property.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
}
