using System;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog
{
    /// <summary>
    /// 日志实体基类。
    /// 使用 <see cref="TimeProvider"/> 获取当前时间，以保证可测试性（可在测试中注入
    /// <see cref="TimeProvider.System"/> 的替代实现来冻结/控制时间），同时避免直接依赖
    /// <c>DateTimeOffset.UtcNow</c> 导致时间不可控的问题。
    /// </summary>
    public abstract class LoggerEntityBase
    {
        private readonly DateTimeOffset _timestamp;

        /// <summary>使用系统时钟创建实体（生产用途）。</summary>
        protected LoggerEntityBase()
            : this(TimeProvider.System)
        {
        }

        /// <summary>注入 <see cref="TimeProvider"/> 创建实体（可测试）。</summary>
        protected LoggerEntityBase(TimeProvider timeProvider)
            : this((timeProvider ?? TimeProvider.System).GetUtcNow())
        {
        }

        /// <summary>直接指定时间戳创建实体（序列化/还原场景）。</summary>
        protected LoggerEntityBase(DateTimeOffset timestamp)
        {
            _timestamp = timestamp;
        }

        public string? RequestId { get; set; }
        public object? Data { get; set; }
        public string? Message { get; set; }

        /// <summary>
        /// 日志时间戳（UTC）
        /// </summary>
        public DateTimeOffset Timestamp => _timestamp;

        /// <summary>
        /// 日志码
        /// </summary>
        public int Code { get; init; }

        /// <summary>
        /// 操作行为，表示日志记录的所属的一种的操作（业务操作）
        /// </summary>
        public string? Action { get; set; }

        /// <summary>
        /// 请求链消耗时间(ms)
        /// </summary>
        public long ElapsedTimeMs { get; set; }
    }    public sealed class InfoLoggerEntity : LoggerEntityBase
    {
        public InfoLoggerEntity() { }
        public InfoLoggerEntity(TimeProvider timeProvider) : base(timeProvider) { }
        public InfoLoggerEntity(DateTimeOffset timestamp) : base(timestamp) { }
    }

    public sealed class ErrorLoggerEntity : LoggerEntityBase
    {
        public ErrorLoggerEntity() { }
        public ErrorLoggerEntity(TimeProvider timeProvider) : base(timeProvider) { }
        public ErrorLoggerEntity(DateTimeOffset timestamp) : base(timestamp) { }

        /// <summary>
        /// 异常信息
        /// </summary>
        public Exception? Exception { get; set; }
    }
}
