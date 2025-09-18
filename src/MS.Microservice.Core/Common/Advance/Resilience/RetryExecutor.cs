using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MS.Microservice.Core.Common.Advance.Resilience
{
    /// <summary>
    /// 重试执行器
    /// </summary>
    public class RetryExecutor(IRetryStrategy strategy, IRetryCondition condition)
    {
        private readonly IRetryStrategy _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        private readonly IRetryCondition _condition = condition ?? throw new ArgumentNullException(nameof(condition));

        private ILogger _logger = NullLogger<RetryExecutor>.Instance;
        public ILogger Logger
        {
            get => _logger;
            set => _logger = value ?? NullLogger<RetryExecutor>.Instance;
        }

        public async Task<T> ExecuteAsync<T>(
            Func<Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            var context = new RetryContext();
            while (true)
            {
                _logger.LogDebug("Retry attempt {Attempt} starting", context.Attempt + 1);
                Exception? lastException;
                try
                {
                    var result = await operation();

                    if (!_condition.ShouldRetry(result, null))
                    {
                        _logger.LogDebug("Operation succeeded on attempt {Attempt}", context.Attempt + 1);
                        return result;
                    }

                    // 结果不满足条件，需要重试
                    lastException = new InvalidOperationException("Result does not meet retry condition");
                    _logger.LogWarning(lastException, "Result does not meet retry condition on attempt {Attempt}", context.Attempt + 1);
                }
                catch (Exception ex)
                {
                    lastException = ex;

                    // 检查异常是否需要重试
                    if (!_condition.ShouldRetry<object?>(null, ex))
                    {
                        _logger.LogError(ex, "Operation failed with non-retriable exception on attempt {Attempt}", context.Attempt + 1);
                        throw;
                    }
                    _logger.LogWarning(ex, "Operation failed with retriable exception on attempt {Attempt}", context.Attempt + 1);
                }

                // 检查是否应该重试
                if (!_strategy.ShouldRetry(context))
                {
                    _logger.LogError(lastException, "Retry attempts exhausted after {Attempts} attempts", context.Attempt + 1);
                    if (lastException != null)
                        throw lastException;
                    throw new InvalidOperationException("Retry attempts exhausted");
                }

                // 等待重试延迟
                var delay = _strategy.GetDelay(context);
                if (delay > TimeSpan.Zero)
                {
                    _logger.LogInformation("Waiting {Delay} before next retry attempt {NextAttempt}", delay, context.Attempt + 2);
                    await Task.Delay(delay, cancellationToken);
                }
                context.Attempt++;
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
