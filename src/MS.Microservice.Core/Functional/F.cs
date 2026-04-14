namespace MS.Microservice.Core.Functional
{
    /// <summary>
    /// 函数式编程核心工厂类，提供 <see cref="Option{T}"/>、<see cref="Either{L, R}"/>、
    /// <see cref="Some{T}"/>、<see cref="NoneType"/> 和 <see cref="Unit"/> 的快捷构造方法。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 建议在项目顶层通过 <c>using static MS.Microservice.Core.Functional.F;</c>
    /// 静态导入，从而直接使用 <c>Some(42)</c>、<c>None</c>、<c>Unit()</c> 等简洁写法。
    /// </para>
    /// <para>
    /// 来源：《C# 函数式编程》第 3 章 — 设计函数签名与类型。
    /// </para>
    /// </remarks>
    public static class F
    {
        /// <summary>
        /// 全局唯一的 None 值，可隐式转换为任意 <see cref="Option{T}"/>。
        /// </summary>
        /// <example>
        /// <code>
        /// Option&lt;string&gt; opt = F.None;
        /// </code>
        /// </example>
        public static NoneType None => default;

        /// <summary>
        /// 创建一个包含 <paramref name="value"/> 的 <see cref="Some{T}"/> 实例。
        /// <see cref="Some{T}"/> 可隐式转换为对应的 <see cref="Option{T}"/>。
        /// </summary>
        /// <typeparam name="T">被包装的值类型。</typeparam>
        /// <param name="value">要包装的非 null 值。</param>
        /// <returns>包含 <paramref name="value"/> 的 <see cref="Some{T}"/>。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="value"/> 为 null 时抛出。</exception>
        /// <example>
        /// <code>
        /// Option&lt;int&gt; opt = F.Some(42);
        /// </code>
        /// </example>
        public static Some<T> Some<T>(T value) => new(value);

        /// <summary>
        /// 创建一个 Left 容器，按约定表示错误分支。
        /// </summary>
        public static Left<L> Left<L>(L value) => new(value);

        /// <summary>
        /// 创建一个 Right 容器，按约定表示成功分支。
        /// </summary>
        public static Right<R> Right<R>(R value) => new(value);

        /// <summary>
        /// 返回 <see cref="Unit"/> 的唯一实例，用于将无返回值的操作（<c>Action</c>）
        /// 提升为可在函数式管道中传递的函数。
        /// </summary>
        /// <returns><see cref="Unit.Default"/>。</returns>
        /// <example>
        /// <code>
        /// Func&lt;string, Unit&gt; log = msg => { Console.WriteLine(msg); return UnitValue; };
        /// </code>
        /// </example>
        public static Unit UnitValue => Functional.Unit.Default;
    }
}
