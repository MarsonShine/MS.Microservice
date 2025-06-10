using System;

namespace MS.Microservice.Core.Common.Advance.Resilience.RetryCondition
{
    /// <summary>
    /// 异常类型重试条件
    /// </summary>
    public class ExceptionTypeRetryCondition(params Type[] exceptionTypes) : IRetryCondition
    {
        private readonly Type[] _exceptionTypes = exceptionTypes ?? throw new ArgumentNullException(nameof(exceptionTypes));

        public bool ShouldRetry<T>(T result, Exception? exception)
        {
            if (exception == null) return false;

            foreach (var type in _exceptionTypes)
            {
                if (type.IsAssignableFrom(exception.GetType()))
                    return true;
            }
            return false;
        }
    }
}
