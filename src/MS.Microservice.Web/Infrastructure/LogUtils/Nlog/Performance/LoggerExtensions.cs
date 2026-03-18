using Microsoft.Extensions.Logging;
using System;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog.Performance
{
    /// <summary>
    /// 高性能日志扩展 —— 使用 .NET 6+ LoggerMessage Source Generator。
    ///
    /// 编译期原理：
    ///   编译器读取 [LoggerMessage] 特性，为每个 partial 方法生成以下代码（示意）：
    ///   - 一个静态只读的 <c>Action&lt;ILogger, T, Exception?&gt;</c> 字段（由 LoggerMessage.Define 创建）
    ///   - 调用方法体中先执行 IsEnabled 检查再调用该委托
    ///   这与手写 LoggerMessage.Define + IsEnabled 守卫完全等价，但由编译器保证正确性，
    ///   无需运行时字典缓存（<see cref="HighPerformanceLog"/>），实现真正的零分配。
    ///
    /// 限制：消息模板必须是编译期常量（字符串字面量或 const）。
    /// 对于运行期动态模板，仍可使用 <see cref="HighPerformanceLog"/>。
    /// </summary>
    public static partial class LoggerExtensions
    {
        // ── Information ────────────────────────────────────────────────────────────

        [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "{Message}")]
        public static partial void LogInfo(this ILogger logger, string message);

        [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "{Message} {Arg1}")]
        public static partial void LogInfo<T1>(this ILogger logger, string message, T1 arg1);

        [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "{Message} {Arg1} {Arg2}")]
        public static partial void LogInfo<T1, T2>(this ILogger logger, string message, T1 arg1, T2 arg2);

        [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "{Message} {Arg1} {Arg2} {Arg3}")]
        public static partial void LogInfo<T1, T2, T3>(this ILogger logger, string message, T1 arg1, T2 arg2, T3 arg3);

        // ── Warning ────────────────────────────────────────────────────────────────

        [LoggerMessage(EventId = 2000, Level = LogLevel.Warning, Message = "{Message}")]
        public static partial void LogWarn(this ILogger logger, string message);

        [LoggerMessage(EventId = 2001, Level = LogLevel.Warning, Message = "{Message} {Arg1}")]
        public static partial void LogWarn<T1>(this ILogger logger, string message, T1 arg1);

        [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "{Message} {Arg1} {Arg2}")]
        public static partial void LogWarn<T1, T2>(this ILogger logger, string message, T1 arg1, T2 arg2);

        [LoggerMessage(EventId = 2003, Level = LogLevel.Warning, Message = "{Message} {Arg1} {Arg2} {Arg3}")]
        public static partial void LogWarn<T1, T2, T3>(this ILogger logger, string message, T1 arg1, T2 arg2, T3 arg3);

        // ── Error ──────────────────────────────────────────────────────────────────

        [LoggerMessage(EventId = 3000, Level = LogLevel.Error, Message = "{Message}")]
        public static partial void LogErr(this ILogger logger, string message, Exception? exception = null);

        [LoggerMessage(EventId = 3001, Level = LogLevel.Error, Message = "{Message} {Arg1}")]
        public static partial void LogErr<T1>(this ILogger logger, string message, T1 arg1, Exception? exception = null);

        [LoggerMessage(EventId = 3002, Level = LogLevel.Error, Message = "{Message} {Arg1} {Arg2}")]
        public static partial void LogErr<T1, T2>(this ILogger logger, string message, T1 arg1, T2 arg2, Exception? exception = null);

        [LoggerMessage(EventId = 3003, Level = LogLevel.Error, Message = "{Message} {Arg1} {Arg2} {Arg3}")]
        public static partial void LogErr<T1, T2, T3>(this ILogger logger, string message, T1 arg1, T2 arg2, T3 arg3, Exception? exception = null);

        // ── Debug ──────────────────────────────────────────────────────────────────

        [LoggerMessage(EventId = 4000, Level = LogLevel.Debug, Message = "{Message}")]
        public static partial void LogDbg(this ILogger logger, string message);

        [LoggerMessage(EventId = 4001, Level = LogLevel.Debug, Message = "{Message} {Arg1}")]
        public static partial void LogDbg<T1>(this ILogger logger, string message, T1 arg1);

        [LoggerMessage(EventId = 4002, Level = LogLevel.Debug, Message = "{Message} {Arg1} {Arg2}")]
        public static partial void LogDbg<T1, T2>(this ILogger logger, string message, T1 arg1, T2 arg2);

        // ── HTTP 请求日志（中间件专用，固定模板） ──────────────────────────────────

        [LoggerMessage(
            EventId = 5000,
            Level = LogLevel.Information,
            Message = "HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs}ms")]
        public static partial void LogHttpRequest(
            this ILogger logger,
            string? method,
            string path,
            int statusCode,
            long elapsedMs);
    }
}
