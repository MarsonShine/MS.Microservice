using MS.Microservice.Core.Common.Advance.Resilience.RetryCondition;
using MS.Microservice.Core.Common.Advance.Resilience.RetryStrategy;
using System;

namespace MS.Microservice.Core.Common.Advance.Resilience
{
    /// <summary>
    /// 重试构建器
    /// </summary>
    public class RetryBuilder
    {
        private IRetryStrategy? _strategy;
        private IRetryCondition? _condition;

        public static RetryBuilder Create() => new();

        /// <summary>
        /// 设置重试策略
        /// </summary>
        public RetryBuilder WithStrategy(IRetryStrategy strategy)
        {
            _strategy = strategy;
            return this;
        }

        /// <summary>
        /// 设置固定次数重试
        /// </summary>
        public RetryBuilder WithFixedCount(int maxRetries, TimeSpan delay = default)
        {
            _strategy = new FixedCountRetryStrategy(maxRetries, delay);
            return this;
        }

        /// <summary>
        /// 设置指数退避重试
        /// </summary>
        public RetryBuilder WithExponentialBackoff(
            int maxRetries,
            TimeSpan baseDelay,
            double multiplier = 2.0,
            TimeSpan maxDelay = default)
        {
            _strategy = new ExponentialBackoffRetryStrategy(maxRetries, baseDelay, multiplier, maxDelay);
            return this;
        }

        /// <summary>
        /// 设置超时重试
        /// </summary>
        public RetryBuilder WithTimeout(TimeSpan timeout, TimeSpan delay = default)
        {
            _strategy = new TimeoutRetryStrategy(timeout, delay);
            return this;
        }

        /// <summary>
        /// 设置重试条件
        /// </summary>
        public RetryBuilder WithCondition(IRetryCondition condition)
        {
            _condition = condition;
            return this;
        }

        /// <summary>
        /// 处理特定异常类型
        /// </summary>
        public RetryBuilder OnException<T>() where T : Exception
        {
            _condition = new ExceptionTypeRetryCondition(typeof(T));
            return this;
        }

        /// <summary>
        /// 处理多种异常类型
        /// </summary>
        public RetryBuilder OnExceptions(params Type[] exceptionTypes)
        {
            _condition = new ExceptionTypeRetryCondition(exceptionTypes);
            return this;
        }

        /// <summary>
        /// 根据结果条件重试
        /// </summary>
        public RetryBuilder OnResult<T>(Func<T, bool> condition)
        {
            _condition = new ResultConditionRetryCondition<T>(condition);
            return this;
        }

        /// <summary>
        /// 根据正则表达式匹配重试
        /// </summary>
        public RetryBuilder OnRegexMatch(string pattern, bool shouldMatchToRetry = false)
        {
            _condition = new RegexMatchRetryCondition(pattern, shouldMatchToRetry);
            return this;
        }

        /// <summary>
        /// 构建重试执行器
        /// </summary>
        public RetryExecutor Build()
        {
            if (_strategy == null)
                throw new InvalidOperationException("Retry strategy must be specified");

            if (_condition == null)
                throw new InvalidOperationException("Retry condition must be specified");

            return new RetryExecutor(_strategy, _condition);
        }
    }
}
