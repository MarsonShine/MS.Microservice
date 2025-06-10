using System;

namespace MS.Microservice.Core.Common.Advance.Resilience.RetryStrategy
{
    public class FixedCountRetryStrategy(int maxRetries, TimeSpan delay = default) : IRetryStrategy
    {
        private readonly int _maxRetries = maxRetries;
        private readonly TimeSpan _delay = delay == default ? TimeSpan.Zero : delay;

        public bool ShouldRetry(int attempt, Exception? exception)
        {
            return attempt <= _maxRetries;
        }

        public TimeSpan GetDelay(int attempt, Exception? exception)
        {
            return _delay;
        }
    }
}
