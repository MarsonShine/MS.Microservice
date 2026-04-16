namespace MS.Microservice.Core.Functional
{
    /// <summary>
    /// 对验证器集合的扩展方法。
    /// </summary>
    public static partial class ValidationExtensions
    {
        extension<T>(IEnumerable<Func<T, Validation<T>>> validators)
        {
            /// <summary>
            /// 将多个验证器聚合为一个，所有验证规则都会被执行，所有错误被收集后统一返回。
            /// <para>
            /// 与 <see cref="Either{L,R}"/> 的 <c>Bind</c> 不同（遇到第一个错误即短路），
            /// 这里使用的是"错误聚合"语义：即使前面的规则已经失败，后续规则仍会执行。
            /// </para>
            /// <para>
            /// 对应《C# 函数式编程》7.6.2 节 HarvestErrors 函数：
            /// <c>IEnumerable&lt;Func&lt;T, Validation&lt;T&gt;&gt;&gt; -&gt; Func&lt;T, Validation&lt;T&gt;&gt;</c>
            /// </para>
            /// </summary>
            /// <example>
            /// <code>
            /// var validateAll = new Func&lt;RegisterAccountCommand, Validation&lt;RegisterAccountCommand&gt;&gt;[]
            /// {
            ///     ValidateAccount,
            ///     ValidatePassword,
            ///     ValidateEmail
            /// }.HarvestErrors();
            ///
            /// var result = validateAll(command);
            /// </code>
            /// </example>
            public Func<T, Validation<T>> HarvestErrors()
                => input =>
                {
                    Validation<T> initial = F.Valid(input);
                    return validators.Aggregate(initial, (accumulated, validator) =>
                    {
                        var current = validator(input);

                        // 两个都通过 → 返回最新的 Valid 结果
                        if (!accumulated.IsInvalid && !current.IsInvalid)
                            return current;

                        // 只有当前验证失败 → 以当前错误作为累积值
                        if (!accumulated.IsInvalid)
                            return current;

                        // 只有已有累积错误 → 保留累积
                        if (!current.IsInvalid)
                            return accumulated;

                        // 两个都失败 → 合并 Details，形成包含所有错误的单一 Invalid
                        return F.Invalid(Error.Validation(
                            "输入校验失败",
                            [.. accumulated.Invalid.DetailsOrEmpty, .. current.Invalid.DetailsOrEmpty]));
                    });
                };
        }
    }
}
