namespace MS.Microservice.Core.Common.Advance.Resilience
{
    using System;

    /// <summary>
    /// 重试条件接口
    /// </summary>
    public interface IRetryCondition
    {
        /// <summary>
        /// 判断是否满足重试条件
        /// </summary>
        /// <param name="result">执行结果</param>
        /// <param name="exception">异常信息</param>
        /// <returns>是否满足重试条件</returns>
        bool ShouldRetry<T>(T result, Exception? exception);
    }
}
