using System;

namespace MS.Microservice.Core.Common.Advance.Resilience.RetryStrategy
{
    /// <summary>
    /// 时间限制重试策略
    /// </summary>
    public class TimeoutRetryStrategy(TimeSpan timeout, TimeSpan delay = default) : IRetryStrategy
    {
        private readonly TimeSpan _timeout = timeout;
        private readonly TimeSpan _delay = delay == default ? TimeSpan.FromSeconds(1) : delay;
        private readonly DateTime _startTime = DateTime.UtcNow;

        public bool ShouldRetry(int attempt, Exception? exception)
        {
            return DateTime.UtcNow - _startTime < _timeout;
        }

        public TimeSpan GetDelay(int attempt, Exception? exception)
        {
            return _delay;
        }
    }
}
