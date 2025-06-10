namespace MS.Microservice.Core.Common.Advance.Resilience
{
    using System;

    /// <summary>
    /// 重试策略接口
    /// </summary>
    public interface IRetryStrategy
    {
        /// <summary>
        /// 是否应该重试
        /// </summary>
        /// <param name="context">重试上下文</param>
        /// <returns>是否应该重试</returns>
        bool ShouldRetry(RetryContext context);

        /// <summary>
        /// 获取下次重试的延迟时间
        /// </summary>
        /// <param name="context">重试上下文</param>
        /// <returns>延迟时间</returns>
        TimeSpan GetDelay(RetryContext context);
    }
}
