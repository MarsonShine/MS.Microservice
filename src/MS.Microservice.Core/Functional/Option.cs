namespace MS.Microservice.Core.Functional
{
    /// <summary>
    /// 表示"无值"的单例类型，用于构造 <see cref="Option{T}"/> 的 None 状态。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 直接使用 <see cref="F.None"/> 工厂属性即可得到此类型的唯一实例，
    /// 再配合 <see cref="Option{T}"/> 的隐式转换运算符即可将其转换为任意泛型的 None。
    /// </para>
    /// <para>
    /// 来源：《C# 函数式编程》第 3.2 节 — 使用 Option 表示可选数据。
    /// </para>
    /// </remarks>
    public readonly struct NoneType
    {
        /// <summary>返回字符串 <c>"None"</c>。</summary>
        public override string ToString() => "None";
    }

    /// <summary>
    /// 包含非 null 值的 Option 容器，表示 Option 的"有值"状态。
    /// </summary>
    /// <typeparam name="T">被包装的值类型。</typeparam>
    /// <remarks>
    /// <para>
    /// 不要直接构造 <see cref="Some{T}"/>，应使用 <see cref="F.Some{T}"/> 工厂方法。
    /// <see cref="Some{T}"/> 不允许包含 <c>null</c>；若需要表示缺失值，请使用 <see cref="F.None"/>。
    /// </para>
    /// <para>
    /// 来源：《C# 函数式编程》第 3.2 节 — 使用 Option 表示可选数据。
    /// </para>
    /// </remarks>
    public readonly struct Some<T>
    {
        /// <summary>被包装的非 null 值。</summary>
        internal T Value { get; }

        internal Some(T value)
        {
            // Some 不允许包含 null；缺失值应通过 None 表示
            ArgumentNullException.ThrowIfNull(value, nameof(value));
            Value = value;
        }

        /// <inheritdoc/>
        public override string ToString() => $"Some({Value})";
    }

    /// <summary>
    /// 表示可能存在、也可能不存在的值。
    /// 用于函数式编程中替代 <c>null</c> 引用和裸 <c>null</c> 检查，消除 <see cref="NullReferenceException"/>。
    /// </summary>
    /// <typeparam name="T">可选值的类型。</typeparam>
    /// <remarks>
    /// <para>
    /// <see cref="Option{T}"/> 有两种状态：
    /// <list type="bullet">
    ///   <item><description><b>Some(value)</b>：包含一个非 null 的值。</description></item>
    ///   <item><description><b>None</b>：表示值不存在。</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 推荐使用 <see cref="F"/> 工厂类创建实例：
    /// <code>
    /// Option&lt;int&gt; some = F.Some(42);
    /// Option&lt;int&gt; none = F.None;
    /// </code>
    /// 或利用隐式转换：
    /// <code>
    /// Option&lt;string&gt; opt = "hello"; // Some("hello")
    /// Option&lt;string&gt; opt2 = null;   // None
    /// </code>
    /// </para>
    /// <para>
    /// 使用 <see cref="Match{R}(Func{R}, Func{T, R})"/> 进行模式匹配：
    /// <code>
    /// string result = opt.Match(
    ///     none: () => "no value",
    ///     some: v => $"value is {v}");
    /// </code>
    /// </para>
    /// <para>
    /// 来源：《C# 函数式编程》第 3.2 ~ 3.4 节 — Option、Map、Bind。
    /// </para>
    /// </remarks>
    public readonly struct Option<T> : IEquatable<Option<T>>
    {
        private readonly T? _value;
        private readonly bool _isSome;

        private Option(T value)
        {
            _isSome = true;
            _value = value;
        }

        /// <summary>
        /// 表示"无值"的静态 None 实例，等同于 <c>default(Option&lt;T&gt;)</c>。
        /// </summary>
        public static readonly Option<T> None = default;

        /// <summary>
        /// 指示当前 Option 是否包含值（Some 状态）。
        /// </summary>
        public bool IsSome => _isSome;

        /// <summary>
        /// 指示当前 Option 是否不含值（None 状态）。
        /// </summary>
        public bool IsNone => !_isSome;

        // ── 隐式转换 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 从 <see cref="NoneType"/> 隐式转换为 None 状态的 <see cref="Option{T}"/>。
        /// 允许直接将 <see cref="F.None"/> 赋值给 Option 变量。
        /// </summary>
        public static implicit operator Option<T>(NoneType _) => default;

        /// <summary>
        /// 从 <see cref="Some{T}"/> 隐式转换为 Some 状态的 <see cref="Option{T}"/>。
        /// </summary>
        public static implicit operator Option<T>(Some<T> some) => new(some.Value);

        /// <summary>
        /// 从值 <typeparamref name="T"/> 隐式转换为 <see cref="Option{T}"/>。
        /// <c>null</c> 值自动变为 None，非 null 值自动变为 Some。
        /// </summary>
        public static implicit operator Option<T>(T? value)
            => value is null ? None : new Option<T>(value);

        // ── 核心操作 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 对 Option 进行模式匹配：
        /// 若为 None 则执行 <paramref name="none"/>，若为 Some 则执行 <paramref name="some"/>。
        /// </summary>
        /// <typeparam name="R">返回结果的类型。</typeparam>
        /// <param name="none">None 分支的处理函数（无参数）。</param>
        /// <param name="some">Some 分支的处理函数（接收内部值）。</param>
        /// <returns>由所选分支产生的结果。</returns>
        /// <remarks>
        /// <c>Match</c> 是 Option 上最基础的操作；所有其他操作（Map、Bind、GetOrElse 等）
        /// 都可以通过 Match 实现，保证了穷举（exhaustive）的模式处理。
        /// </remarks>
        public R Match<R>(Func<R> none, Func<T, R> some)
            => _isSome ? some(_value!) : none();

        // ── 相等性 ────────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public bool Equals(Option<T> other)
        {
            if (IsNone && other.IsNone) return true;
            if (IsSome && other.IsSome) return EqualityComparer<T>.Default.Equals(_value, other._value);
            return false;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Option<T> other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => _isSome ? _value!.GetHashCode() : 0;

        /// <summary>
        /// Some 状态返回 <c>"Some(value)"</c>，None 状态返回 <c>"None"</c>。
        /// </summary>
        public override string ToString() => _isSome ? $"Some({_value})" : "None";

        /// <summary>两个 Option 相等当且仅当状态相同且（若都为 Some）内部值相等。</summary>
        public static bool operator ==(Option<T> left, Option<T> right) => left.Equals(right);

        /// <summary>两个 Option 不相等。</summary>
        public static bool operator !=(Option<T> left, Option<T> right) => !left.Equals(right);
    }
}
