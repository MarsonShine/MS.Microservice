﻿using System;

namespace MS.Microservice.Core.Common.Advance.Resilience.RetryStrategy
{
    /// <summary>
    /// 指数退避重试策略
    /// </summary>
    public class ExponentialBackoffRetryStrategy(
        int maxRetries,
        TimeSpan baseDelay,
        double multiplier = 2.0,
        TimeSpan maxDelay = default) : IRetryStrategy
    {
        private readonly int _maxRetries = maxRetries;
        private readonly TimeSpan _baseDelay = baseDelay;
        private readonly TimeSpan _maxDelay = maxDelay == default ? TimeSpan.FromMinutes(5) : maxDelay;
        private readonly double _multiplier = multiplier;

        public bool ShouldRetry(RetryContext context)
        {
            return context.Attempt <= _maxRetries;
        }

        public TimeSpan GetDelay(RetryContext context)
        {
            var delay = TimeSpan.FromMilliseconds(
                _baseDelay.TotalMilliseconds * Math.Pow(_multiplier, context.Attempt - 1));

            return delay > _maxDelay ? _maxDelay : delay;
        }
    }
}
