using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog.Performance
{
    /// <summary>
    /// 高性能日志消息定义缓存。
    /// 使用 LoggerMessage.Define 预编译日志消息模板，避免每次日志调用的分配。
    /// 缓存键为 (LogLevel, formatString) 组合，确保同一模板只编译一次。
    /// </summary>
    public static class HighPerformanceLog
    {
        // 按泛型参数数量分桶缓存，避免类型擦除问题
        private static class Cache<T1>
        {
            internal static readonly ConcurrentDictionary<(LogLevel, string), Action<ILogger, T1, Exception?>> Delegates = new();
        }

        private static class Cache<T1, T2>
        {
            internal static readonly ConcurrentDictionary<(LogLevel, string), Action<ILogger, T1, T2, Exception?>> Delegates = new();
        }

        private static class Cache<T1, T2, T3>
        {
            internal static readonly ConcurrentDictionary<(LogLevel, string), Action<ILogger, T1, T2, T3, Exception?>> Delegates = new();
        }

        private static class Cache<T1, T2, T3, T4>
        {
            internal static readonly ConcurrentDictionary<(LogLevel, string), Action<ILogger, T1, T2, T3, T4, Exception?>> Delegates = new();
        }

        private static class Cache<T1, T2, T3, T4, T5>
        {
            internal static readonly ConcurrentDictionary<(LogLevel, string), Action<ILogger, T1, T2, T3, T4, T5, Exception?>> Delegates = new();
        }

        private static class Cache<T1, T2, T3, T4, T5, T6>
        {
            internal static readonly ConcurrentDictionary<(LogLevel, string), Action<ILogger, T1, T2, T3, T4, T5, T6, Exception?>> Delegates = new();
        }

        public static Action<ILogger, T1, Exception?> GetOrDefine<T1>(LogLevel level, string format)
        {
            return Cache<T1>.Delegates.GetOrAdd((level, format), static key =>
                LoggerMessage.Define<T1>(key.Item1, new EventId(0), key.Item2));
        }

        public static Action<ILogger, T1, T2, Exception?> GetOrDefine<T1, T2>(LogLevel level, string format)
        {
            return Cache<T1, T2>.Delegates.GetOrAdd((level, format), static key =>
                LoggerMessage.Define<T1, T2>(key.Item1, new EventId(0), key.Item2));
        }

        public static Action<ILogger, T1, T2, T3, Exception?> GetOrDefine<T1, T2, T3>(LogLevel level, string format)
        {
            return Cache<T1, T2, T3>.Delegates.GetOrAdd((level, format), static key =>
                LoggerMessage.Define<T1, T2, T3>(key.Item1, new EventId(0), key.Item2));
        }

        public static Action<ILogger, T1, T2, T3, T4, Exception?> GetOrDefine<T1, T2, T3, T4>(LogLevel level, string format)
        {
            return Cache<T1, T2, T3, T4>.Delegates.GetOrAdd((level, format), static key =>
                LoggerMessage.Define<T1, T2, T3, T4>(key.Item1, new EventId(0), key.Item2));
        }

        public static Action<ILogger, T1, T2, T3, T4, T5, Exception?> GetOrDefine<T1, T2, T3, T4, T5>(LogLevel level, string format)
        {
            return Cache<T1, T2, T3, T4, T5>.Delegates.GetOrAdd((level, format), static key =>
                LoggerMessage.Define<T1, T2, T3, T4, T5>(key.Item1, new EventId(0), key.Item2));
        }

        public static Action<ILogger, T1, T2, T3, T4, T5, T6, Exception?> GetOrDefine<T1, T2, T3, T4, T5, T6>(LogLevel level, string format)
        {
            return Cache<T1, T2, T3, T4, T5, T6>.Delegates.GetOrAdd((level, format), static key =>
                LoggerMessage.Define<T1, T2, T3, T4, T5, T6>(key.Item1, new EventId(0), key.Item2));
        }
    }
}
