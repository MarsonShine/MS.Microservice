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

        public bool ShouldRetry(RetryContext context)
        {
            return context.TotalElapsed < _timeout;
        }

        public TimeSpan GetDelay(RetryContext context)
        {
            return _delay;
        }
    }
}
