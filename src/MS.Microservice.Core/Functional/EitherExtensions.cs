namespace MS.Microservice.Core.Functional
{
    /// <summary>
    /// Either 的核心组合能力。
    /// </summary>
    public static partial class EitherExtensions
    {
        public static Task<Either<L, R>> AsTask<L, R>(this Either<L, R> either)
            => Task.FromResult(either);

        public static Task<Either<L, R>> RightAsync<L, R>(R value)
            => Task.FromResult((Either<L, R>)F.Right(value));

        public static Task<Either<L, R>> LeftAsync<L, R>(L value)
            => Task.FromResult((Either<L, R>)F.Left(value));

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

        public static async Task<Either<L, RR>> Map<L, R, RR>(this Task<Either<L, R>> eitherTask, Func<R, RR> mapper)
            => (await eitherTask).Map(mapper);

        public static async Task<Either<LL, R>> MapLeft<L, R, LL>(this Task<Either<L, R>> eitherTask, Func<L, LL> mapper)
            => (await eitherTask).MapLeft(mapper);

        public static async Task<Either<L, RR>> Bind<L, R, RR>(this Task<Either<L, R>> eitherTask, Func<R, Either<L, RR>> binder)
            => (await eitherTask).Bind(binder);

        public static async Task<Either<L, RR>> BindAsync<L, R, RR>(this Task<Either<L, R>> eitherTask, Func<R, Task<Either<L, RR>>> binder)
        {
            var either = await eitherTask;
            return await either.BindAsync(binder);
        }

        public static async Task<Either<L, RR>> MapAsync<L, R, RR>(this Task<Either<L, R>> eitherTask, Func<R, Task<RR>> mapper)
        {
            var either = await eitherTask;
            return await either.MapAsync(mapper);
        }

        public static async Task<Either<L, R>> Tap<L, R>(this Task<Either<L, R>> eitherTask, Action<R> effect)
            => (await eitherTask).Tap(effect);

        public static async Task<Either<L, R>> TapAsync<L, R>(this Task<Either<L, R>> eitherTask, Func<R, Task> effect)
        {
            var either = await eitherTask;
            return await either.TapAsync(effect);
        }

        public static async Task<Either<L, R>> Where<L, R>(this Task<Either<L, R>> eitherTask, Func<R, bool> predicate, Func<R, L> leftFactory)
            => (await eitherTask).Where(predicate, leftFactory);

        public static async Task<T> MatchAsync<L, R, T>(this Task<Either<L, R>> eitherTask, Func<L, Task<T>> left, Func<R, Task<T>> right)
            => await (await eitherTask).MatchAsync(left, right);

        public static async Task<T> MatchAsync<L, R, T>(this Task<Either<L, R>> eitherTask, Func<L, T> left, Func<R, T> right)
            => (await eitherTask).Match(left, right);

        extension<L, R>(Either<L, R> either)
        {
            public Either<LL,RR> Map<LL,RR>(Func<L,LL> left, Func<R,RR> right) 
                => either.Match<Either<LL, RR>>(
                    left: l => F.Left(left(l)), 
                    right: r => F.Right(right(r)));

            public Either<L, RR> Map<RR>(Func<R, RR> mapper)
                => either.Match<Either<L, RR>>(
                    left: l => F.Left<L>(l),
                    right: r => F.Right(mapper(r)));

            public Either<LL, R> MapLeft<LL>(Func<L, LL> mapper)
                => either.Match<Either<LL, R>>(
                    left: l => F.Left(mapper(l)),
                    right: r => F.Right<R>(r));

            public Either<L, RR> Bind<RR>(Func<R, Either<L, RR>> binder)
                => either.Match<Either<L, RR>>(
                    left: l => F.Left<L>(l),
                    right: binder);

            public Task<Either<L, RR>> BindAsync<RR>(Func<R, Task<Either<L, RR>>> binder)
                => either.Match(
                    left: l => Task.FromResult((Either<L, RR>)F.Left<L>(l)),
                    right: binder);

            public Task<Either<L, RR>> MapAsync<RR>(Func<R, Task<RR>> mapper)
                => either.Match<Task<Either<L, RR>>>(
                    left: l => Task.FromResult((Either<L, RR>)F.Left<L>(l)),
                    right: async r => F.Right(await mapper(r)));

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
                    left: l => F.Left<L>(l),
                    right: r => predicate(r)
                        ? F.Right(r)
                        : F.Left(leftFactory(r)));

            public Task<T> MatchAsync<T>(Func<L, Task<T>> left, Func<R, Task<T>> right)
                => either.Match(left, right);

            public Either<L, RR> Select<RR>(Func<R, RR> mapper)
                => either.Map(mapper);

            public Either<L, RR> SelectMany<R2, RR>(Func<R, Either<L, R2>> bind, Func<R, R2, RR> project)
                => either.Bind(r => bind(r).Map(r2 => project(r, r2)));
        }
    }
}
