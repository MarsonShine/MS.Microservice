namespace MS.Microservice.Core.Functional
{
    /// <summary>
    /// 为 <see cref="Option{T}"/> 提供函数式编程核心操作的扩展方法集合。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 涵盖以下操作类别：
    /// <list type="bullet">
    ///   <item><description><b>Map</b>（函子 / Functor）：对 Some 中的值进行变换。</description></item>
    ///   <item><description><b>Bind</b>（单子 / Monad）：链接多个可能返回 None 的操作。</description></item>
    ///   <item><description><b>Match</b>：穷举式模式匹配。</description></item>
    ///   <item><description><b>GetOrElse / OrElse</b>：提取值或提供备用值。</description></item>
    ///   <item><description><b>Where</b>（Filter）：按谓词过滤。</description></item>
    ///   <item><description><b>ForEach</b>：对 Some 执行副作用。</description></item>
    ///   <item><description><b>AsEnumerable</b>：将 Option 转为序列。</description></item>
    ///   <item><description><b>Select / SelectMany</b>：支持 LINQ 查询语法。</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 来源：《C# 函数式编程》第 3.4 节 — 使用 Map 和 Bind 组合 Option 值。
    /// </para>
    /// </remarks>
    public static class OptionExtensions
    {
        // ── Map（函子操作）────────────────────────────────────────────────────────

        /// <summary>
        /// <b>Map</b>：将 Option 内部的值通过函数 <paramref name="f"/> 变换为新类型，
        /// 返回包含变换结果的 Option。
        /// </summary>
        /// <typeparam name="T">原始值类型。</typeparam>
        /// <typeparam name="R">变换后的值类型。</typeparam>
        /// <param name="opt">被操作的 Option。</param>
        /// <param name="f">值变换函数。</param>
        /// <returns>
        /// 若 <paramref name="opt"/> 为 Some(t)，则返回 Some(f(t))；
        /// 若为 None，则直接返回 None，<paramref name="f"/> 不会被执行。
        /// </returns>
        /// <remarks>
        /// <para>
        /// Map 体现了"函子"（Functor）的核心概念：在保持容器结构不变的情况下，
        /// 对容器内的值应用一个普通函数。
        /// </para>
        /// <para>
        /// 来源：《C# 函数式编程》第 3.4 节 — Map（函子操作）。
        /// </para>
        /// </remarks>
        public static Option<R> Map<T, R>(
            this Option<T> opt,
            Func<T, R> f)
            => opt.Match(
                none: () => (Option<R>)F.None,
                some: t => (Option<R>)F.Some(f(t)));

        // ── Bind（单子操作）───────────────────────────────────────────────────────

        /// <summary>
        /// <b>Bind</b>（又称 FlatMap / SelectMany）：
        /// 将 Option 内部的值传递给一个返回 Option 的函数 <paramref name="f"/>，
        /// 并将结果"展平"为单层 Option。
        /// </summary>
        /// <typeparam name="T">原始值类型。</typeparam>
        /// <typeparam name="R">结果 Option 的值类型。</typeparam>
        /// <param name="opt">被操作的 Option。</param>
        /// <param name="f">接收值并返回 Option 的函数（Cross-world 函数）。</param>
        /// <returns>
        /// 若 <paramref name="opt"/> 为 Some(t)，则返回 f(t)（可能为 Some 或 None）；
        /// 若为 None，则直接返回 None，<paramref name="f"/> 不会被执行。
        /// </returns>
        /// <remarks>
        /// <para>
        /// Bind 体现了"单子"（Monad）的核心概念：将多个可能失败（返回 None）的操作
        /// 串联在一起，只要任意一步返回 None，整条链路即短路为 None。
        /// </para>
        /// <para>
        /// 来源：《C# 函数式编程》第 3.4 节 — Bind（单子操作）。
        /// </para>
        /// </remarks>
        public static Option<R> Bind<T, R>(
            this Option<T> opt,
            Func<T, Option<R>> f)
            => opt.Match(
                none: () => F.None,
                some: t => f(t));

        // ── Match（模式匹配）──────────────────────────────────────────────────────

        /// <summary>
        /// <b>Match</b> 重载：以值而非函数的方式提供 None 分支的结果。
        /// </summary>
        /// <typeparam name="T">Option 内部值类型。</typeparam>
        /// <typeparam name="R">返回结果类型。</typeparam>
        /// <param name="opt">被操作的 Option。</param>
        /// <param name="none">None 分支返回的直接值。</param>
        /// <param name="some">Some 分支处理函数。</param>
        public static R Match<T, R>(
            this Option<T> opt,
            R none,
            Func<T, R> some)
            => opt.Match(
                none: () => none,
                some: some);

        // ── GetOrElse / OrElse ────────────────────────────────────────────────────

        /// <summary>
        /// <b>GetOrElse</b>：获取 Some 中的值；若为 None，则返回 <paramref name="defaultValue"/>。
        /// </summary>
        public static T GetOrElse<T>(
            this Option<T> opt,
            T defaultValue)
            => opt.Match(
                none: () => defaultValue,
                some: t => t);

        /// <summary>
        /// <b>GetOrElse</b>：获取 Some 中的值；若为 None，则调用 <paramref name="fallback"/> 获取默认值。
        /// </summary>
        /// <remarks>延迟计算版本，仅在 Option 为 None 时才执行 <paramref name="fallback"/>，适合昂贵的默认值计算。</remarks>
        public static T GetOrElse<T>(
            this Option<T> opt,
            Func<T> fallback)
            => opt.Match(
                none: fallback,
                some: t => t);

        /// <summary>
        /// <b>OrElse</b>：若当前 Option 为 None，则用备用 <paramref name="fallback"/> Option 替代。
        /// </summary>
        public static Option<T> OrElse<T>(
            this Option<T> opt,
            Option<T> fallback)
            => opt.Match(
                none: () => fallback,
                some: _ => opt);

        /// <summary>
        /// <b>OrElse</b>：若当前 Option 为 None，则调用 <paramref name="fallback"/> 获取备用 Option。
        /// </summary>
        /// <remarks>延迟计算版本，仅在 Option 为 None 时才执行 <paramref name="fallback"/>。</remarks>
        public static Option<T> OrElse<T>(
            this Option<T> opt,
            Func<Option<T>> fallback)
            => opt.Match(
                none: fallback,
                some: _ => opt);

        // ── ForEach（副作用）─────────────────────────────────────────────────────

        /// <summary>
        /// <b>ForEach</b>：若 Option 为 Some，则对内部值执行 <paramref name="action"/> 副作用操作。
        /// </summary>
        /// <returns>
        /// 返回 <see cref="Unit"/> 以保持函数式风格（避免出现无返回值的函数）。
        /// </returns>
        /// <remarks>
        /// ForEach 是唯一允许产生副作用（side-effect）的操作，
        /// 通常用于与外部系统（日志、IO 等）交互的最终步骤（边界处），
        /// 不应在纯业务逻辑的中间环节使用。
        /// </remarks>
        public static Unit ForEach<T>(
            this Option<T> opt,
            Action<T> action)
            => opt.Match(
                none: () => Unit.Default,
                some: t => { action(t); return Unit.Default; });

        // ── Where（过滤）─────────────────────────────────────────────────────────

        /// <summary>
        /// <b>Where</b>（Filter）：若 Option 为 Some 且内部值满足 <paramref name="predicate"/>，
        /// 则保留原值；否则返回 None。
        /// </summary>
        /// <remarks>
        /// 来源：《C# 函数式编程》第 3.4 节 — Where/Filter 操作。
        /// </remarks>
        public static Option<T> Where<T>(
            this Option<T> opt,
            Func<T, bool> predicate)
            => opt.Match(
                none: () => F.None,
                some: t => predicate(t) ? opt : (Option<T>)F.None);

        // ── AsEnumerable ─────────────────────────────────────────────────────────

        /// <summary>
        /// 将 Option 转换为 <see cref="IEnumerable{T}"/>：
        /// Some(t) 转换为只含一个元素的序列 [t]，None 转换为空序列 []。
        /// </summary>
        /// <remarks>
        /// 此转换在需要将 Option 与 LINQ 序列操作（如 SelectMany）混用时非常有用。
        /// </remarks>
        public static IEnumerable<T> AsEnumerable<T>(this Option<T> opt)
            => opt.Match(
                none: Enumerable.Empty<T>,
                some: t => Enumerable.Repeat(t, 1));

        // ── LINQ 查询语法支持 ─────────────────────────────────────────────────────

        /// <summary>
        /// 支持 LINQ 查询语法中的 <c>select</c> 子句（等同于 Map 操作）。
        /// </summary>
        /// <example>
        /// <code>
        /// var result = from age in maybeAge
        ///              select age + 1;
        /// </code>
        /// </example>
        public static Option<R> Select<T, R>(
            this Option<T> opt,
            Func<T, R> f)
            => opt.Map(f);

        /// <summary>
        /// 支持 LINQ 查询语法中的多级 <c>from</c> 子句（等同于 Bind + Map 操作）。
        /// </summary>
        /// <example>
        /// <code>
        /// var result = from age in maybeAge
        ///              from email in maybeEmail
        ///              select $"{email} is {age}";
        /// </code>
        /// </example>
        public static Option<RR> SelectMany<T, R, RR>(
            this Option<T> opt,
            Func<T, Option<R>> bind,
            Func<T, R, RR> project)
            => opt.Match(
                none: () => (Option<RR>)F.None,
                some: t => bind(t).Match(
                    none: () => (Option<RR>)F.None,
                    some: r => (Option<RR>)F.Some(project(t, r))));
    }
}
