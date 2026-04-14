namespace MS.Microservice.Core.Functional
{
    /// <summary>
    /// Exceptional 的异常分支容器。
    /// </summary>
    public readonly struct ExceptionThrown
    {
        internal Exception Value { get; }

        internal ExceptionThrown(Exception value)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(value));
            Value = value;
        }

        public override string ToString() => $"ExceptionThrown({Value.Message})";
    }

    /// <summary>
    /// Exceptional 的成功分支容器。
    /// </summary>
    public readonly struct Success<T>
    {
        internal T Value { get; }

        internal Success(T value)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(value));
            Value = value;
        }

        public override string ToString() => $"Success({Value})";
    }

    /// <summary>
    /// Either&lt;Exception, T&gt; 的特定版本：ExceptionThrown 表示失败，Success 表示成功。
    /// </summary>
    public readonly struct Exceptional<T> : IEquatable<Exceptional<T>>
    {
        private readonly Either<Exception, T> _either;

        private Exceptional(Either<Exception, T> either) => _either = either;

        public bool IsSuccess => _either.IsRight;

        public bool IsException => _either.IsLeft;

        public Exception Exception => IsException ? _either.Left : throw new InvalidOperationException("Exceptional 处于 Success 状态，无法读取 Exception。");

        public T Success => IsSuccess ? _either.Right : throw new InvalidOperationException("Exceptional 处于 Exception 状态，无法读取 Success。");

        public static implicit operator Exceptional<T>(ExceptionThrown exception)
            => new((Either<Exception, T>)F.Left(exception.Value));

        public static implicit operator Exceptional<T>(Success<T> success)
            => new((Either<Exception, T>)F.Right(success.Value));

        public static implicit operator Exceptional<T>(Either<Exception, T> either)
            => new(either);

        public static implicit operator Either<Exception, T>(Exceptional<T> exceptional)
            => exceptional._either;

        public R Match<R>(Func<Exception, R> exception, Func<T, R> success)
            => _either.Match(exception, success);

        public Exceptional<R> Map<R>(Func<T, R> mapper)
            => _either.Map(mapper);

        public Exceptional<R> Bind<R>(Func<T, Exceptional<R>> binder)
            => Match(
                exception: ex => (Exceptional<R>)F.ExceptionThrown(ex),
                success: binder);

        public bool Equals(Exceptional<T> other) => _either.Equals(other._either);

        public override bool Equals(object? obj) => obj is Exceptional<T> other && Equals(other);

        public override int GetHashCode() => _either.GetHashCode();

        public override string ToString() => Match(
            exception: ex => $"ExceptionThrown({ex.Message})",
            success: value => $"Success({value})");

        public static bool operator ==(Exceptional<T> left, Exceptional<T> right) => left.Equals(right);

        public static bool operator !=(Exceptional<T> left, Exceptional<T> right) => !left.Equals(right);
    }
}
