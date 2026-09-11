using System.Collections.Concurrent;

namespace MS.Microservice.Core.Reflection;

/// <summary>某种编译实现的静态开关，类型对象恒为单例，比较引用即可。</summary>
public interface IPropertyAccessorStrategy
{
    /// <summary>把类型体对象的属性依次写入目标集合，返回写入后的元素个数。</summary>
    Func<object, ICollection<string>, int> CompilePopulate(Type type);

    /// <summary>返回该类型全部可读属性的编译访问器，顺序与过滤规则遵循 <see cref="PropertyAccessors" /> 的契约。</summary>
    PropertyAccessor[] CreateAccessors(Type type);

    /// <summary>为属性生成读取委托；属性不可读时返回 <see langword="null" />。</summary>
    Func<object, object?>? CompileGetter(System.Reflection.PropertyInfo property);

    /// <summary>为属性生成写入委托；属性不可写时返回 <see langword="null" />。</summary>
    Action<object, object?>? CompileSetter(System.Reflection.PropertyInfo property);

    /// <summary>按公开无参构造函数生成工厂；没有可用构造函数时返回 <see langword="null" />。</summary>
    Func<object>? CompileFactory(Type type);
}

/// <summary>按（实现，类型）缓存属性的编译访问器，把"元数据发现"限制在每个类型一次。</summary>
/// <remarks>
/// 顺序契约：返回的数组按 <see cref="Type.GetProperties()" /> 的元数据顺序排列，同一类型的多次调用顺序稳定
/// （由两个实现的同源测试锁定）。顺序拼接 URL、表头等场景可以直接依赖它。
/// 过滤契约：只包含公开实例、可读、且无索引参数的属性，与逐属性反射的原实现一致。
/// 缓存以实现为键，因此切换 <see cref="Use" /> 后同一类型会用新实现重新编译，两个实现可以并存对照。
/// Native AOT 与裁剪场景不适用：两种实现都在运行时生成代码。
/// </remarks>
public static class PropertyAccessors
{
    private static readonly ConcurrentDictionary<(IPropertyAccessorStrategy Strategy, Type Type), PropertyAccessor[]> Cache = new();
    private static readonly ConcurrentDictionary<(IPropertyAccessorStrategy Strategy, Type Type), Func<object, ICollection<string>, int>> PopulateCache = new();
    private static readonly ConcurrentDictionary<(IPropertyAccessorStrategy Strategy, Type Type), Func<object>?> FactoryCache = new();
    private static volatile IPropertyAccessorStrategy active = new IlPropertyAccessorStrategy();

    /// <summary>当前生效的编译实现，默认 IL；切换到表达树实现可逐字节对照两种产物。</summary>
    public static IPropertyAccessorStrategy Active => active;

    /// <summary>IL 版实现（<c>DynamicMethod</c> + <c>ILGenerator</c>），默认策略。</summary>
    public static IPropertyAccessorStrategy Il { get; } = new IlPropertyAccessorStrategy();

    /// <summary>表达树版实现，作为 IL 的对照与回退。</summary>
    public static IPropertyAccessorStrategy Expression { get; } = new ExpressionPropertyAccessorStrategy();

    /// <summary>切换编译实现；下一次取用同一类型时会用新实现重新编译并单独缓存。</summary>
    public static void Use(IPropertyAccessorStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        active = strategy;
    }

    /// <summary>返回该类型的属性访问器，顺序与过滤规则见类型注释。</summary>
    public static PropertyAccessor[] Get(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var strategy = active;
        if (Cache.TryGetValue((strategy, type), out var accessors)) return accessors;
        return Cache.GetOrAdd((strategy, type), static key => key.Strategy.CreateAccessors(key.Type));
    }

    /// <summary>返回把该类型体写入目标集合的委托，返回值为写入后的元素个数。</summary>
    public static Func<object, ICollection<string>, int> Materializer(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var strategy = active;
        if (PopulateCache.TryGetValue((strategy, type), out var populate)) return populate;
        return PopulateCache.GetOrAdd((strategy, type), static key => key.Strategy.CompilePopulate(key.Type));
    }

    /// <summary>返回通过公开无参构造函数创建实例的委托；该类型没有可用构造函数时返回 <see langword="null" />。</summary>
    public static Func<object>? Factory(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var strategy = active;
        if (FactoryCache.TryGetValue((strategy, type), out var factory)) return factory;
        return FactoryCache.GetOrAdd((strategy, type), static key => key.Strategy.CompileFactory(key.Type));
    }
}
