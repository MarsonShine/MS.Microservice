using System;

namespace MS.Microservice.Core.Common.Advance.Resilience.RetryCondition
{
    /// <summary>
    /// 结果条件重试
    /// </summary>
    public class ResultConditionRetryCondition<TResult>(Func<TResult, bool> condition) : IRetryCondition
    {
        private readonly Func<TResult, bool> _condition = condition ?? throw new ArgumentNullException(nameof(condition));

        public bool ShouldRetry<T>(T result, Exception? exception)
        {
            if (result is TResult typedResult)
                return _condition(typedResult);
            return false;
        }
    }
}
