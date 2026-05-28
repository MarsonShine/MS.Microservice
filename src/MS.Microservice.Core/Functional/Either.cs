using System.Diagnostics.CodeAnalysis;

namespace MS.Microservice.Core.Functional
{
    /// <summary>
    /// Either 的 Left 容器，按约定表示错误分支。
    /// </summary>
    public readonly struct Left<L>
    {
        internal L Value { get; }

        internal Left(L value)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(value));
            Value = value;
        }

        public override string ToString() => $"Left({Value})";
    }

    /// <summary>
    /// Either 的 Right 容器，按约定表示成功分支。
    /// </summary>
    public readonly struct Right<R>
    {
        internal R Value { get; }

        internal Right(R value)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(value));
            Value = value;
        }

        public override string ToString() => $"Right({Value})";
    }

    /// <summary>
    /// 表示两种可能输出之一：Left(错误) 或 Right(成功)。
    /// </summary>
    public readonly struct Either<L, R> : IEquatable<Either<L, R>>
    {
        private readonly L? _left;
        private readonly R? _right;
        private readonly bool _isRight;

        private Either(L left)
        {
            _left = left;
            _right = default;
            _isRight = false;
        }

        private Either(R right)
        {
            _left = default;
            _right = right;
            _isRight = true;
        }

        [MemberNotNullWhen(true, nameof(_right))]
        [MemberNotNullWhen(false, nameof(_left))]
        public bool IsRight => _isRight;

        public bool IsLeft => !_isRight;

        public L Left => IsLeft ? _left! : throw new InvalidOperationException("Either 处于 Right 状态，无法读取 Left。");

        public R Right => IsRight ? _right! : throw new InvalidOperationException("Either 处于 Left 状态，无法读取 Right。");

        public static implicit operator Either<L, R>(Left<L> left) => new(left.Value);

        public static implicit operator Either<L, R>(Right<R> right) => new(right.Value);

        public T Match<T>(Func<L, T> left, Func<R, T> right)
            => IsRight ? right(_right!) : left(_left!);

        public bool Equals(Either<L, R> other)
        {
            if (IsLeft && other.IsLeft)
            {
                return EqualityComparer<L>.Default.Equals(_left, other._left);
            }

            if (IsRight && other.IsRight)
            {
                return EqualityComparer<R>.Default.Equals(_right, other._right);
            }

            return false;
        }

        public override bool Equals(object? obj) => obj is Either<L, R> other && Equals(other);

        public override int GetHashCode()
            => IsRight
                ? HashCode.Combine(true, _right)
                : HashCode.Combine(false, _left);

        public override string ToString()
            => IsRight ? $"Right({_right})" : $"Left({_left})";

        public static bool operator ==(Either<L, R> left, Either<L, R> right) => left.Equals(right);

        public static bool operator !=(Either<L, R> left, Either<L, R> right) => !left.Equals(right);
    }
}
