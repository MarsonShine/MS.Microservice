using System;

namespace MS.Microservice.Core.Common.Advance.Resilience.RetryStrategy
{
    public class FixedCountRetryStrategy(int maxRetries, TimeSpan delay = default) : IRetryStrategy
    {
        private readonly int _maxRetries = maxRetries;
        private readonly TimeSpan _delay = delay == default ? TimeSpan.Zero : delay;

        public bool ShouldRetry(RetryContext context)
        {
            return context.Attempt <= _maxRetries;
        }

        public TimeSpan GetDelay(RetryContext context)
        {
            return _delay;
        }
    }
}
