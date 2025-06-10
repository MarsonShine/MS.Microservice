using System;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Common.Advance.Resilience
{
    /// <summary>
    /// 重试执行器
    /// </summary>
    public class RetryExecutor(IRetryStrategy strategy, IRetryCondition condition)
    {
        private readonly IRetryStrategy _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        private readonly IRetryCondition _condition = condition ?? throw new ArgumentNullException(nameof(condition));

        public async Task<T> ExecuteAsync<T>(
            Func<Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            int attempt = 1;
            while (true)
            {
                Exception? lastException;
                try
                {
                    var result = await operation();

                    if (!_condition.ShouldRetry(result, null))
                    {
                        return result;
                    }

                    // 结果不满足条件，需要重试
                    lastException = new InvalidOperationException("Result does not meet retry condition");
                }
                catch (Exception ex)
                {
                    lastException = ex;

                    // 检查异常是否需要重试
                    if (!_condition.ShouldRetry<object?>(null, ex))
                    {
                        throw;
                    }
                }

                // 检查是否应该重试
                if (!_strategy.ShouldRetry(attempt, lastException))
                {
                    if (lastException != null)
                        throw lastException;
                    throw new InvalidOperationException("Retry attempts exhausted");
                }

                // 等待重试延迟
                var delay = _strategy.GetDelay(attempt, lastException);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }

                attempt++;
            }
        }

        public async Task ExecuteAsync(
            Func<Task> operation,
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async () =>
            {
                await operation();
                return true;
            }, cancellationToken);
        }

        public T Execute<T>(Func<T> operation)
        {
            return ExecuteAsync(() => Task.FromResult(operation())).GetAwaiter().GetResult();
        }

        public void Execute(Action operation)
        {
            Execute(() =>
            {
                operation();
                return true;
            });
        }
    }
}
