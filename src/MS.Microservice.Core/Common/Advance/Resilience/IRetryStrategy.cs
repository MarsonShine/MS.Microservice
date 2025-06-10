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
        /// <param name="attempt">当前尝试次数（从1开始）</param>
        /// <param name="exception">发生的异常</param>
        /// <returns>是否应该重试</returns>
        bool ShouldRetry(int attempt, Exception? exception);

        /// <summary>
        /// 获取下次重试的延迟时间
        /// </summary>
        /// <param name="attempt">当前尝试次数</param>
        /// <param name="exception">发生的异常</param>
        /// <returns>延迟时间</returns>
        TimeSpan GetDelay(int attempt, Exception? exception);
    }
}
