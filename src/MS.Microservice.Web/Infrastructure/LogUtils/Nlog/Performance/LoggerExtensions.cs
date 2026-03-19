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

        private static readonly Action<ILogger, string, object?, Exception?> _logInfo1 =
            LoggerMessage.Define<string, object?>(LogLevel.Information, new EventId(1001), "{Message} {Arg1}");
        public static void LogInfo(this ILogger logger, string message, object? arg1) =>
            _logInfo1(logger, message, arg1, null);

        private static readonly Action<ILogger, string, object?, object?, Exception?> _logInfo2 =
            LoggerMessage.Define<string, object?, object?>(LogLevel.Information, new EventId(1002), "{Message} {Arg1} {Arg2}");
        public static void LogInfo(this ILogger logger, string message, object? arg1, object? arg2) =>
            _logInfo2(logger, message, arg1, arg2, null);

        private static readonly Action<ILogger, string, object?, object?, object?, Exception?> _logInfo3 =
            LoggerMessage.Define<string, object?, object?, object?>(LogLevel.Information, new EventId(1003), "{Message} {Arg1} {Arg2} {Arg3}");
        public static void LogInfo(this ILogger logger, string message, object? arg1, object? arg2, object? arg3) =>
            _logInfo3(logger, message, arg1, arg2, arg3, null);

        // ── Warning ────────────────────────────────────────────────────────────────

        [LoggerMessage(EventId = 2000, Level = LogLevel.Warning, Message = "{Message}")]
        public static partial void LogWarn(this ILogger logger, string message);

        private static readonly Action<ILogger, string, object?, Exception?> _logWarn1 =
            LoggerMessage.Define<string, object?>(LogLevel.Warning, new EventId(2001), "{Message} {Arg1}");
        public static void LogWarn(this ILogger logger, string message, object? arg1) =>
            _logWarn1(logger, message, arg1, null);

        private static readonly Action<ILogger, string, object?, object?, Exception?> _logWarn2 =
            LoggerMessage.Define<string, object?, object?>(LogLevel.Warning, new EventId(2002), "{Message} {Arg1} {Arg2}");
        public static void LogWarn(this ILogger logger, string message, object? arg1, object? arg2) =>
            _logWarn2(logger, message, arg1, arg2, null);

        private static readonly Action<ILogger, string, object?, object?, object?, Exception?> _logWarn3 =
            LoggerMessage.Define<string, object?, object?, object?>(LogLevel.Warning, new EventId(2003), "{Message} {Arg1} {Arg2} {Arg3}");
        public static void LogWarn(this ILogger logger, string message, object? arg1, object? arg2, object? arg3) =>
            _logWarn3(logger, message, arg1, arg2, arg3, null);

        // ── Error ──────────────────────────────────────────────────────────────────

        [LoggerMessage(EventId = 3000, Level = LogLevel.Error, Message = "{Message}")]
        public static partial void LogErr(this ILogger logger, string message, Exception? exception = null);

        private static readonly Action<ILogger, string, object?, Exception?> _logErr1 =
            LoggerMessage.Define<string, object?>(LogLevel.Error, new EventId(3001), "{Message} {Arg1}");
        public static void LogErr(this ILogger logger, string message, object? arg1, Exception? exception = null) =>
            _logErr1(logger, message, arg1, exception);

        private static readonly Action<ILogger, string, object?, object?, Exception?> _logErr2 =
            LoggerMessage.Define<string, object?, object?>(LogLevel.Error, new EventId(3002), "{Message} {Arg1} {Arg2}");
        public static void LogErr(this ILogger logger, string message, object? arg1, object? arg2, Exception? exception = null) =>
            _logErr2(logger, message, arg1, arg2, exception);

        private static readonly Action<ILogger, string, object?, object?, object?, Exception?> _logErr3 =
            LoggerMessage.Define<string, object?, object?, object?>(LogLevel.Error, new EventId(3003), "{Message} {Arg1} {Arg2} {Arg3}");
        public static void LogErr(this ILogger logger, string message, object? arg1, object? arg2, object? arg3, Exception? exception = null) =>
            _logErr3(logger, message, arg1, arg2, arg3, exception);

        // ── Debug ──────────────────────────────────────────────────────────────────

        [LoggerMessage(EventId = 4000, Level = LogLevel.Debug, Message = "{Message}")]
        public static partial void LogDbg(this ILogger logger, string message);

        private static readonly Action<ILogger, string, object?, Exception?> _logDbg1 =
            LoggerMessage.Define<string, object?>(LogLevel.Debug, new EventId(4001), "{Message} {Arg1}");
        public static void LogDbg(this ILogger logger, string message, object? arg1) =>
            _logDbg1(logger, message, arg1, null);

        private static readonly Action<ILogger, string, object?, object?, Exception?> _logDbg2 =
            LoggerMessage.Define<string, object?, object?>(LogLevel.Debug, new EventId(4002), "{Message} {Arg1} {Arg2}");
        public static void LogDbg(this ILogger logger, string message, object? arg1, object? arg2) =>
            _logDbg2(logger, message, arg1, arg2, null);

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
