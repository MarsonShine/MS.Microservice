namespace MS.Microservice.Core.Functional;

public static partial class F
{
    public static Task<T> Async<T>(T t) => Task.FromResult(t);

    #region Task<T> extensions
    extension<T>(Task<T> task)
    {
        public async Task<TResult> Map<TResult>(Func<T, TResult> f) => f(await task);

        public async Task<TResult> Bind<TResult>(Func<T, Task<TResult>> f) => await f(await task);

        public async Task<T> OrElse(Func<Task<T>> fallback)
        {
            try
            {
                return await task.ConfigureAwait(false);
            }
            catch
            {
                return await fallback().ConfigureAwait(false);
            }
        }

        public Task<T> Recover(Func<Exception, T> fallback) =>
            task.ContinueWith(t =>
                t.Status == TaskStatus.Faulted
                ? fallback(t.Exception!)
                : t.Result);

        /// <summary>
        /// 在成功和失败的情况下都会接受到一个函数来处理结果，成功时传入结果，失败时传入异常
        /// </summary>
        /// <param name="Faulted"></param>
        /// <param name="Completed"></param>
        /// <returns></returns>
        public Task<TResult> Map<TResult>(Func<Exception, TResult> Faulted, Func<T, TResult> Completed) =>
            task.ContinueWith(t =>
                t.Status == TaskStatus.Faulted
                ? Faulted(t.Exception!)
                : Completed(t.Result));

        public Task<TResult> Select<TResult>(Func<T, TResult> map) => task.ContinueWith(t => map(t.Result));
        public async Task<TResult> SelectMany<TResult>(Func<T, Task<TResult>> bind)
        {
            var value = await task.ConfigureAwait(false);
            return await bind(value).ConfigureAwait(false);
        }

        public async Task<TResult> SelectMany<TResult>(Func<Unit, Task<TResult>> bind)
        {
            await task.ConfigureAwait(false);
            return await bind(Unit.Default).ConfigureAwait(false);
        }
    }
    #endregion

    #region Task extensions
    extension(Task task)
    {
        public async Task<V> SelectMany<U, V>(
            Func<Unit, Task<U>> bind,
            Func<Unit, U, V> project)
        {
            await task.ConfigureAwait(false);
            var u = await bind(Unit.Default).ConfigureAwait(false);
            return project(Unit.Default, u);
        }

        public async Task<R> Select<R>(Func<Unit, R> map)
        {
            await task.ConfigureAwait(false);
            return map(Unit.Default);
        }
    }
    #endregion

    extension<T, R>(Task<Func<T, R>> f)
    {
        public async Task<R> Apply(Task<T> arg) => (await f)(await arg);
    }

    extension<T1, T2, R>(Task<Func<T1, T2, R>> f)
    {
        public Task<Func<T2, R>> Apply(Task<T1> arg) => f.Map(func => func.Curry()).Apply(arg);
    }

    public static async Task<T> Retry<T>(int retries, int delayMillis, Func<Task<T>> start)
    {
        try
        {
            return await start().ConfigureAwait(false);
        }
        catch when (retries > 0)
        {
            await Task.Delay(delayMillis).ConfigureAwait(false);
            return await Retry(retries - 1, delayMillis * 2, start).ConfigureAwait(false);
        }
    }
}
