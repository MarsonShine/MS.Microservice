namespace MS.Microservice.Core.Functional
{
    /// <summary>
    /// 表示"无意义返回值"的类型，用于函数式编程中替代 <c>void</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 在函数式编程中，函数应当是纯粹的映射：每一个输入都对应唯一的输出。
    /// 但 C# 中的 <c>void</c> 方法不返回任何值，导致无法将它们当作一等函数（first-class function）
    /// 传递给 <see cref="Func{TResult}"/> 或 <see cref="Func{T, TResult}"/> 等委托。
    /// </para>
    /// <para>
    /// <b>Unit</b> 解决了这一问题：它是一个只有一个实例的类型，
    /// 任何"无意义"的返回值都可以用 <see cref="Default"/> 表示。
    /// 将 <c>Action</c> 包装为返回 <see cref="Unit"/> 的 <c>Func</c>，
    /// 即可在函数式管道（pipeline）中统一处理。
    /// </para>
    /// <para>
    /// 来源：《C# 函数式编程》第 3.1 节 — 使用 Unit 表示无意义的值。
    /// </para>
    /// </remarks>
    public readonly struct Unit : IEquatable<Unit>
    {
        /// <summary>
        /// <see cref="Unit"/> 的唯一实例。
        /// 等同于 <c>default(Unit)</c>。
        /// </summary>
        public static readonly Unit Default = default;

        /// <inheritdoc/>
        public bool Equals(Unit other) => true;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Unit;

        /// <inheritdoc/>
        public override int GetHashCode() => 0;

        /// <summary>
        /// 返回函数式编程中惯用的 Unit 字符串表示 <c>"()"</c>。
        /// </summary>
        public override string ToString() => "()";

        /// <summary>两个 <see cref="Unit"/> 值永远相等。</summary>
        public static bool operator ==(Unit left, Unit right) => true;

        /// <summary>两个 <see cref="Unit"/> 值永远不会“不相等”。</summary>
        public static bool operator !=(Unit left, Unit right) => false;
    }
}
