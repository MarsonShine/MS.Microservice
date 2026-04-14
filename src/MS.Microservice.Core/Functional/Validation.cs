namespace MS.Microservice.Core.Functional
{
    /// <summary>
    /// Validation 的 Invalid 容器，包装结构化错误。
    /// </summary>
    public readonly struct Invalid
    {
        internal Error Value { get; }

        internal Invalid(Error value)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(value));
            Value = value;
        }

        public override string ToString() => $"Invalid({Value})";
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
        private readonly Either<Error, T> _either;

        private Validation(Either<Error, T> either) => _either = either;

        public bool IsValid => _either.IsRight;

        public bool IsInvalid => _either.IsLeft;

        public Error Invalid => IsInvalid ? _either.Left : throw new InvalidOperationException("Validation 处于 Valid 状态，无法读取 Invalid。");

        public T Valid => IsValid ? _either.Right : throw new InvalidOperationException("Validation 处于 Invalid 状态，无法读取 Valid。");

        public static implicit operator Validation<T>(Invalid invalid)
            => new((Either<Error, T>)F.Left(invalid.Value));

        public static implicit operator Validation<T>(Valid<T> valid)
            => new((Either<Error, T>)F.Right(valid.Value));

        public static implicit operator Validation<T>(Either<Error, T> either)
            => new(either);

        public static implicit operator Either<Error, T>(Validation<T> validation)
            => validation._either;

        public R Match<R>(Func<Error, R> invalid, Func<T, R> valid)
            => _either.Match(invalid, valid);

        public Validation<R> Map<R>(Func<T, R> mapper)
            => _either.Map(mapper);

        public Validation<R> Bind<R>(Func<T, Validation<R>> binder)
            => Match(
                invalid: error => (Validation<R>)F.Invalid(error),
                valid: binder);

        public bool Equals(Validation<T> other) => _either.Equals(other._either);

        public override bool Equals(object? obj) => obj is Validation<T> other && Equals(other);

        public override int GetHashCode() => _either.GetHashCode();

        public override string ToString() => Match(
            invalid: error => $"Invalid({error})",
            valid: value => $"Valid({value})");

        public static bool operator ==(Validation<T> left, Validation<T> right) => left.Equals(right);

        public static bool operator !=(Validation<T> left, Validation<T> right) => !left.Equals(right);
    }
}
