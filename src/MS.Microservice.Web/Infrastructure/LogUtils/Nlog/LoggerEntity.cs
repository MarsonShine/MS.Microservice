using System;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog
{
    public abstract class LoggerEntityBase
    {
        public string? RequestId { get; set; }
        public object? Data { get; set; }
        public string? Message { get; set; }
        public string Date { get; } = DateTime.Now.ToShortDateString();
        public string Time { get; } = DateTime.Now.ToString("hh:mm:ss:ffff");
        /// <summary>
        /// 日志码
        /// </summary>
        public int Code { get; }
        /// <summary>
        /// 操作行为，表示日志记录的所属的一种的操作（业务操作）
        /// </summary>
        public string? Action { get; set; }
        /// <summary>
        /// 请求链消耗时间
        /// </summary>
        public int ElapsedTime { get; set; }
    }

    public class InfoLoggerEntty : LoggerEntityBase
    {

    }

    public class ErrorLoggerEntity : LoggerEntityBase
    {

    }
}
