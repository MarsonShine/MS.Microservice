namespace MS.Microservice.Core.Functional
{
    /// <summary>
    /// Either 的核心组合能力。
    /// </summary>
    public static partial class EitherExtensions
    {
        public static Either<Error, R> Try<R>(Func<R> operation, string code = "unexpected")
        {
            try
            {
                return F.Right(operation());
            }
            catch (Exception ex)
            {
                return F.Left(Error.FromException(ex, code));
            }
        }

        public static Either<Error, Unit> Try(Action operation, string code = "unexpected")
            => Try(() =>
            {
                operation();
                return Unit.Default;
            }, code);

        public static async Task<Either<Error, R>> TryAsync<R>(Func<Task<R>> operation, string code = "unexpected")
        {
            try
            {
                return F.Right(await operation());
            }
            catch (Exception ex)
            {
                return F.Left(Error.FromException(ex, code));
            }
        }

        public static Task<Either<Error, Unit>> TryAsync(Func<Task> operation, string code = "unexpected")
            => TryAsync(async () =>
            {
                await operation();
                return Unit.Default;
            }, code);

        extension<L, R>(Either<L, R> either)
        {
            public Either<L, RR> Map<RR>(Func<R, RR> mapper)
                => either.Match<Either<L, RR>>(
                    left: l => (Either<L, RR>)F.Left<L>(l),
                    right: r => (Either<L, RR>)F.Right(mapper(r)));

            public Either<LL, R> MapLeft<LL>(Func<L, LL> mapper)
                => either.Match<Either<LL, R>>(
                    left: l => (Either<LL, R>)F.Left(mapper(l)),
                    right: r => (Either<LL, R>)F.Right<R>(r));

            public Either<L, RR> Bind<RR>(Func<R, Either<L, RR>> binder)
                => either.Match<Either<L, RR>>(
                    left: l => (Either<L, RR>)F.Left<L>(l),
                    right: binder);

            public Task<Either<L, RR>> BindAsync<RR>(Func<R, Task<Either<L, RR>>> binder)
                => either.Match(
                    left: l => Task.FromResult((Either<L, RR>)F.Left<L>(l)),
                    right: binder);

            public Task<Either<L, RR>> MapAsync<RR>(Func<R, Task<RR>> mapper)
                => either.Match<Task<Either<L, RR>>>(
                    left: l => Task.FromResult((Either<L, RR>)F.Left<L>(l)),
                    right: async r => (Either<L, RR>)F.Right(await mapper(r)));

            public Either<L, R> Tap(Action<R> effect)
            {
                if (either.IsRight)
                {
                    effect(either.Right);
                }

                return either;
            }

            public async Task<Either<L, R>> TapAsync(Func<R, Task> effect)
            {
                if (either.IsRight)
                {
                    await effect(either.Right);
                }

                return either;
            }

            public R GetOrElse(R fallback)
                => either.Match(
                    left: _ => fallback,
                    right: r => r);

            public R GetOrElse(Func<L, R> fallback)
                => either.Match(
                    left: fallback,
                    right: r => r);

            public Either<L, R> OrElse(Either<L, R> fallback)
                => either.Match(
                    left: _ => fallback,
                    right: _ => either);

            public Either<L, R> Where(Func<R, bool> predicate, Func<R, L> leftFactory)
                => either.Match<Either<L, R>>(
                    left: l => (Either<L, R>)F.Left<L>(l),
                    right: r => predicate(r)
                        ? (Either<L, R>)F.Right(r)
                        : (Either<L, R>)F.Left(leftFactory(r)));

            public Task<T> MatchAsync<T>(Func<L, Task<T>> left, Func<R, Task<T>> right)
                => either.Match(left, right);

            public Either<L, RR> Select<RR>(Func<R, RR> mapper)
                => either.Map(mapper);

            public Either<L, RR> SelectMany<R2, RR>(Func<R, Either<L, R2>> bind, Func<R, R2, RR> project)
                => either.Bind(r => bind(r).Map(r2 => project(r, r2)));
        }
    }
}
