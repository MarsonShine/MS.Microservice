namespace MS.Microservice.Core.Functional
{
    using static F;
    public static partial class F
    {
        public static Validation<T> Valid<T>(T value) => new(value);

        // 创建表示不正确状态的Validation
        public static Validation.Invalid Invalid(params Error[] errors) => new(errors);
        public static Validation<R> Invalid<R>(params Error[] errors) => new Validation.Invalid(errors);
        public static Validation.Invalid Invalid(IEnumerable<Error> errors) => new(errors);
        public static Validation<R> Invalid<R>(IEnumerable<Error> errors) => new Validation.Invalid(errors);
    }

    /// <summary>
    /// Validation 的 Valid 容器，包装成功值。
    /// </summary>
    public readonly struct Valid<T>
    {
        internal T Value { get; }

        internal Valid(T value)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(value));
            Value = value;
        }

        public override string ToString() => $"Valid({Value})";
    }

    /// <summary>
    /// Either&lt;Error, T&gt; 的特定版本：Invalid 表示失败，Valid 表示成功。
    /// </summary>
    public readonly struct Validation<T> : IEquatable<Validation<T>>
    {
        internal IEnumerable<Error> Errors { get; }
        private readonly T? _value;

        public bool IsValid { get; }
        public bool IsInvalid => !IsValid;

        /// <summary>当 <see cref="IsValid"/> 为 true 时，返回内部包装的有效值。</summary>
        public T Value => IsValid ? _value! : throw new InvalidOperationException("Validation is Invalid.");

        /// <summary><see cref="Value"/> 的别名，与书中原始 API 保持一致。</summary>
        public T Valid => Value;

        /// <summary>当 <see cref="IsInvalid"/> 为 true 时，返回对应的 <see cref="Validation.Invalid"/> 结构。</summary>
        public Validation.Invalid Invalid => new(Errors);

        private Validation(IEnumerable<Error> errors)
        {
            IsValid = false;
            Errors = errors;
            _value = default;
        }

        internal Validation(T right)
        {
            IsValid = true;
            _value = right;
            Errors = [];
        }

        // the Return function for Validation
        public static Func<T, Validation<T>> Return = t => Valid(t);

        public static Validation<T> Fail(IEnumerable<Error> errors)
           => new(errors);

        public static Validation<T> Fail(params Error[] errors)
           => new(errors.AsEnumerable());

        public static implicit operator Validation<T>(Error error)
         => new([error]);
        public static implicit operator Validation<T>(Validation.Invalid left)
           => new(left.Errors);
        public static implicit operator Validation<T>(T right) => Valid(right);

        public TR Match<TR>(Func<IEnumerable<Error>, TR> invalid, Func<T, TR> valid)
            => IsValid ? valid(_value!) : invalid(Errors);

        public Unit Match(Action<IEnumerable<Error>> invalid, Action<T> valid)
            => Match(invalid.ToFunc(), valid.ToFunc());

        public Validation<R> Bind<R>(Func<T, Validation<R>> binder)
            => Match(
                invalid: error => (Validation<R>)F.Invalid(error),
                valid: binder);

        public IEnumerator<T?> AsEnumerable()
        {
            if (IsValid) yield return _value;
        }

        public override string ToString()
           => IsValid
              ? $"Valid({Value})"
              : $"Invalid([{string.Join(", ", Errors)}])";

        public bool Equals(Validation<T> other)
        {
            if (IsValid != other.IsValid) return false;
            if (IsValid) return EqualityComparer<T>.Default.Equals(_value, other._value);
            return Errors.SequenceEqual(other.Errors);
        }

        public override bool Equals(object? obj)
            => obj is Validation<T> other && Equals(other);

        public override int GetHashCode()
        {
            if (IsValid)
                return HashCode.Combine(true, EqualityComparer<T>.Default.GetHashCode(_value!));

            var hc = new HashCode();
            hc.Add(false);
            foreach (var err in Errors)
                hc.Add(err);
            return hc.ToHashCode();
        }

        public static bool operator ==(Validation<T> left, Validation<T> right) => left.Equals(right);
        public static bool operator !=(Validation<T> left, Validation<T> right) => !left.Equals(right);
    }

    public static class Validation
    {
        public readonly struct Invalid
        {
            internal IEnumerable<Error> Errors { get; }
            internal Invalid(IEnumerable<Error> errors) => Errors = errors;

            /// <summary>取第一个 <see cref="Error"/> 的 <see cref="Error.Code"/>，便于单错误场景下的断言。</summary>
            public string Code => Errors.FirstOrDefault()?.Code ?? string.Empty;

            /// <summary>取第一个 <see cref="Error"/> 的 <see cref="Error.Message"/>。</summary>
            public string Message => Errors.FirstOrDefault()?.Message ?? string.Empty;

            public override string ToString() => $"Invalid({string.Join(", ", Errors)})";
        }

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
                        if (accumulated.IsValid && current.IsValid)
                            return current;

                        // 只有当前验证失败 → 以当前错误作为累积值
                        if (accumulated.IsValid)
                            return current;

                        // 只有已有累积错误 → 保留累积
                        if (current.IsValid)
                            return accumulated;

                        // 两个都失败 → 合并所有 Error，形成包含所有错误的单一 Invalid
                        return F.Invalid(accumulated.Invalid.Errors.Concat(current.Invalid.Errors));
                    });
                };
        }

        extension<T, R>(Validation<Func<T, R>> valF)
        {
            public Validation<R> Apply(Validation<T> valT)
                => valF.Match(
                    valid: (f) => valT.Match(
                        valid: (t) => F.Valid(f(t)),
                        invalid: (err) => F.Invalid<R>(err)
                    ),
                    invalid: (errF) => valT.Match(
                        valid: (_) => F.Invalid<R>(errF),
                        invalid: (errT) => F.Invalid<R>(errF.Concat(errT))
                        ));
        }

        extension<T>(Validation<T> opt)
        {
            public Validation<R> Bind<R>(Func<T, Validation<R>> f) 
                => opt.Match(
                    invalid: (err) => Invalid(err),
                    valid: r => f(r));

            public T GetOrThrow()
             => opt.Match(
                (errs) => throw new InvalidOperationException($"Validation failed with errors: {string.Join(", ", errs)}"),
                (t) => t);

            public T GetOrElse(T defaultValue)
                => opt.Match(
                    (errs) => defaultValue,
                    (t) => t);

            public T GetOrElse(Func<T> fallback)
                => opt.Match(
                    (errs) => fallback(),
                    (t) => t);
        }


        public static Validation<Func<T2, R>> Apply<T1, T2, R>
         (this Validation<Func<T1, T2, R>> @this, Validation<T1> arg)
         => Apply(@this.Map(F.Curry), arg);

        public static Validation<Func<T2, T3, R>> Apply<T1, T2, T3, R>
           (this Validation<Func<T1, T2, T3, R>> @this, Validation<T1> arg)
           => Apply(@this.Map(F.CurryFirst), arg);

        public static Validation<Func<T2, T3, T4, R>> Apply<T1, T2, T3, T4, R>
           (this Validation<Func<T1, T2, T3, T4, R>> @this, Validation<T1> arg)
           => Apply(@this.Map(F.CurryFirst), arg);

        public static Validation<Func<T2, T3, T4, T5, R>> Apply<T1, T2, T3, T4, T5, R>
           (this Validation<Func<T1, T2, T3, T4, T5, R>> @this, Validation<T1> arg)
           => Apply(@this.Map(F.CurryFirst), arg);

        public static Validation<Func<T2, T3, T4, T5, T6, R>> Apply<T1, T2, T3, T4, T5, T6, R>
           (this Validation<Func<T1, T2, T3, T4, T5, T6, R>> @this, Validation<T1> arg)
           => Apply(@this.Map(F.CurryFirst), arg);

        public static Validation<Func<T2, T3, T4, T5, T6, T7, R>> Apply<T1, T2, T3, T4, T5, T6, T7, R>
           (this Validation<Func<T1, T2, T3, T4, T5, T6, T7, R>> @this, Validation<T1> arg)
           => Apply(@this.Map(F.CurryFirst), arg);

        public static Validation<Func<T2, T3, T4, T5, T6, T7, T8, R>> Apply<T1, T2, T3, T4, T5, T6, T7, T8, R>
           (this Validation<Func<T1, T2, T3, T4, T5, T6, T7, T8, R>> @this, Validation<T1> arg)
           => Apply(@this.Map(F.CurryFirst), arg);

        public static Validation<Func<T2, T3, T4, T5, T6, T7, T8, T9, R>> Apply<T1, T2, T3, T4, T5, T6, T7, T8, T9, R>
           (this Validation<Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, R>> @this, Validation<T1> arg)
           => Apply(@this.Map(F.CurryFirst), arg);

        public static Validation<RR> Map<R, RR>
         (this Validation<R> @this, Func<R, RR> f)
         => @this.IsValid
            ? Valid(f(@this.Value!))
            : Invalid(@this.Errors);

        public static Validation<Func<T2, R>> Map<T1, T2, R>(this Validation<T1> @this
           , Func<T1, T2, R> func)
            => @this.Map(func.Curry());

        public static Validation<Unit> ForEach<R>
           (this Validation<R> @this, Action<R> act)
           => Map(@this, act.ToFunc());

        public static Validation<T> Do<T>
           (this Validation<T> @this, Action<T> action)
        {
            @this.ForEach(action);
            return @this;
        }

        // LINQ

        public static Validation<R> Select<T, R>(this Validation<T> @this
           , Func<T, R> map) => @this.Map(map);

        public static Validation<RR> SelectMany<T, R, RR>(this Validation<T> @this
           , Func<T, Validation<R>> bind, Func<T, R, RR> project)
           => @this.Match(
              invalid: (err) => Invalid(err),
              valid: (t) => bind(t).Match(
                 invalid: (err) => Invalid(err),
                 valid: (r) => Valid(project(t, r))));
    }
}
