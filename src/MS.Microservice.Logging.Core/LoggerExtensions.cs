using Microsoft.Extensions.Logging;

namespace MS.Microservice.Logging.Core;

/// <summary>
/// High-performance logging extensions based on <see cref="LoggerMessageAttribute"/>.
/// </summary>
public static partial class LoggerExtensions
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "{Message}")]
    public static partial void LogInfo(this ILogger logger, string message);

    private static readonly Action<ILogger, string, object?, Exception?> LogInfo1Message =
        LoggerMessage.Define<string, object?>(LogLevel.Information, new EventId(1001), "{Message} {Arg1}");

    public static void LogInfo(this ILogger logger, string message, object? arg1) =>
        LogInfo1Message(logger, message, arg1, null);

    private static readonly Action<ILogger, string, object?, object?, Exception?> LogInfo2Message =
        LoggerMessage.Define<string, object?, object?>(LogLevel.Information, new EventId(1002), "{Message} {Arg1} {Arg2}");

    public static void LogInfo(this ILogger logger, string message, object? arg1, object? arg2) =>
        LogInfo2Message(logger, message, arg1, arg2, null);

    private static readonly Action<ILogger, string, object?, object?, object?, Exception?> LogInfo3Message =
        LoggerMessage.Define<string, object?, object?, object?>(LogLevel.Information, new EventId(1003), "{Message} {Arg1} {Arg2} {Arg3}");

    public static void LogInfo(this ILogger logger, string message, object? arg1, object? arg2, object? arg3) =>
        LogInfo3Message(logger, message, arg1, arg2, arg3, null);

    [LoggerMessage(EventId = 2000, Level = LogLevel.Warning, Message = "{Message}")]
    public static partial void LogWarn(this ILogger logger, string message);

    private static readonly Action<ILogger, string, object?, Exception?> LogWarn1Message =
        LoggerMessage.Define<string, object?>(LogLevel.Warning, new EventId(2001), "{Message} {Arg1}");

    public static void LogWarn(this ILogger logger, string message, object? arg1) =>
        LogWarn1Message(logger, message, arg1, null);

    private static readonly Action<ILogger, string, object?, object?, Exception?> LogWarn2Message =
        LoggerMessage.Define<string, object?, object?>(LogLevel.Warning, new EventId(2002), "{Message} {Arg1} {Arg2}");

    public static void LogWarn(this ILogger logger, string message, object? arg1, object? arg2) =>
        LogWarn2Message(logger, message, arg1, arg2, null);

    private static readonly Action<ILogger, string, object?, object?, object?, Exception?> LogWarn3Message =
        LoggerMessage.Define<string, object?, object?, object?>(LogLevel.Warning, new EventId(2003), "{Message} {Arg1} {Arg2} {Arg3}");

    public static void LogWarn(this ILogger logger, string message, object? arg1, object? arg2, object? arg3) =>
        LogWarn3Message(logger, message, arg1, arg2, arg3, null);

    [LoggerMessage(EventId = 3000, Level = LogLevel.Error, Message = "{Message}")]
    public static partial void LogErr(this ILogger logger, string message, Exception? exception = null);

    private static readonly Action<ILogger, string, object?, Exception?> LogErr1Message =
        LoggerMessage.Define<string, object?>(LogLevel.Error, new EventId(3001), "{Message} {Arg1}");

    public static void LogErr(this ILogger logger, string message, object? arg1, Exception? exception = null) =>
        LogErr1Message(logger, message, arg1, exception);

    private static readonly Action<ILogger, string, object?, object?, Exception?> LogErr2Message =
        LoggerMessage.Define<string, object?, object?>(LogLevel.Error, new EventId(3002), "{Message} {Arg1} {Arg2}");

    public static void LogErr(this ILogger logger, string message, object? arg1, object? arg2, Exception? exception = null) =>
        LogErr2Message(logger, message, arg1, arg2, exception);

    private static readonly Action<ILogger, string, object?, object?, object?, Exception?> LogErr3Message =
        LoggerMessage.Define<string, object?, object?, object?>(LogLevel.Error, new EventId(3003), "{Message} {Arg1} {Arg2} {Arg3}");

    public static void LogErr(this ILogger logger, string message, object? arg1, object? arg2, object? arg3, Exception? exception = null) =>
        LogErr3Message(logger, message, arg1, arg2, arg3, exception);

    [LoggerMessage(EventId = 4000, Level = LogLevel.Debug, Message = "{Message}")]
    public static partial void LogDbg(this ILogger logger, string message);

    private static readonly Action<ILogger, string, object?, Exception?> LogDbg1Message =
        LoggerMessage.Define<string, object?>(LogLevel.Debug, new EventId(4001), "{Message} {Arg1}");

    public static void LogDbg(this ILogger logger, string message, object? arg1) =>
        LogDbg1Message(logger, message, arg1, null);

    private static readonly Action<ILogger, string, object?, object?, Exception?> LogDbg2Message =
        LoggerMessage.Define<string, object?, object?>(LogLevel.Debug, new EventId(4002), "{Message} {Arg1} {Arg2}");

    public static void LogDbg(this ILogger logger, string message, object? arg1, object? arg2) =>
        LogDbg2Message(logger, message, arg1, arg2, null);

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