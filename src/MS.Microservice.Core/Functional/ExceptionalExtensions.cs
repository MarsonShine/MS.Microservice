namespace MS.Microservice.Core.Functional
{
    /// <summary>
    /// Exceptional 的辅助工厂与异步包装能力。
    /// </summary>
    public static class ExceptionalExtensions
    {
        public static Exceptional<T> Try<T>(Func<T> operation)
        {
            try
            {
                return F.Success(operation());
            }
            catch (Exception ex)
            {
                return F.ExceptionThrown(ex);
            }
        }

        public static Exceptional<Unit> Try(Action operation)
            => Try(() =>
            {
                operation();
                return Unit.Default;
            });

        public static async Task<Exceptional<T>> TryAsync<T>(Func<Task<T>> operation)
        {
            try
            {
                return F.Success(await operation());
            }
            catch (Exception ex)
            {
                return F.ExceptionThrown(ex);
            }
        }

        public static Task<Exceptional<Unit>> TryAsync(Func<Task> operation)
            => TryAsync(async () =>
            {
                await operation();
                return Unit.Default;
            });
    }
}
