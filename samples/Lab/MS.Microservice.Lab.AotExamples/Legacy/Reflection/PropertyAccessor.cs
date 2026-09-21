using System.Reflection;

namespace MS.Microservice.Lab.AotExamples.Legacy.Reflection;

/// <summary>某个类型上一个属性的编译访问器：元数据只读取一次，读取值不再经过反射。</summary>
/// <param name="Property">原始反射元数据，供需要特性、<see cref="MemberInfo.MetadataToken" /> 等信息的调用方使用。</param>
/// <param name="Name">属性名。</param>
/// <param name="PropertyType">属性声明的类型，可枚举性、格式化等判断都应基于它。</param>
/// <param name="GetValue">编译后的读取委托；属性不可读时为 <see langword="null" />。</param>
/// <param name="SetValue">编译后的写入委托；属性不可写或为只读（如 <c>init</c>）时为 <see langword="null" />。</param>
public readonly record struct PropertyAccessor(
    PropertyInfo Property,
    string Name,
    Type PropertyType,
    Func<object, object?>? GetValue,
    Action<object, object?>? SetValue);
