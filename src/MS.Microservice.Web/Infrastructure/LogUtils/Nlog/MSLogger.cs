using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog
{
    /// <summary>
    /// 自定义 ILogger 实现，用于从 LogState 中捕获结构化日志属性。
    /// 职责单一：仅作为 ILogger 转发日志到 NLog，不再混合 IReadOnlyList 语义。
    /// 性能优化：
    ///   - 缓存 NLog.Logger 实例，避免每次 Log/IsEnabled 调用时的字典查找
    ///   - 使用 for 循环（非 foreach）遍历结构化属性，避免迭代器分配
    ///   - 使用 StringValues 索引器访问 header，避免 ToString() 分配
    ///   - 使用 Stopwatch.GetElapsedTime 高精度计时
    /// </summary>
    public sealed class MSLoggerEvent : ILogger
    {
        private readonly IHttpContextAccessor? _accessor;
        private readonly NLog.Logger _nlogLogger;

        public MSLoggerEvent(string categoryName, IHttpContextAccessor accessor)
        {
            _accessor = accessor;
            // 缓存 NLog Logger 实例（NLog 内部也有缓存，但直接持有引用可省去查找开销）
            _nlogLogger = NLog.LogManager.GetLogger(categoryName);
        }

        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var nlogLevel = ConvertLogLevel(logLevel);

            if (!_nlogLogger.IsEnabled(nlogLevel))
                return;

            var message = formatter(state, exception);
            var logEventInfo = NLog.LogEventInfo.Create(nlogLevel, _nlogLogger.Name, exception, message);

            // 将结构化属性零拷贝传递给 NLog（for 循环避免迭代器分配）
            if (state is IReadOnlyList<KeyValuePair<string, object>> properties)
            {
                for (var i = 0; i < properties.Count; i++)
                {
                    var prop = properties[i];
                    if (!string.IsNullOrEmpty(prop.Key) && prop.Key != "{OriginalFormat}")
                    {
                        logEventInfo.Properties[prop.Key] = prop.Value;
                    }
                }
            }

            // 从 HttpContext 注入请求上下文属性
            var httpContext = _accessor?.HttpContext;
            if (httpContext is not null)
            {
                // 使用 StringValues 索引器避免 ToString() 分配
                if (httpContext.Request.Headers.TryGetValue("requestId", out var requestId) && requestId.Count > 0)
                    logEventInfo.Properties["requestId"] = requestId[0]!;

                // 中间件存储的是 Stopwatch.GetTimestamp()，使用对应 API 计算耗时
                if (httpContext.Items.TryGetValue("ElapsedTime", out var elapsed) && elapsed is long startTimestamp)
                {
                    var durationMs = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                    logEventInfo.Properties["elapsedTime"] = durationMs;
                }
            }

            _nlogLogger.Log(logEventInfo);
        }

        bool ILogger.IsEnabled(LogLevel logLevel)
        {
            if (logLevel == LogLevel.None) return false;
            return _nlogLogger.IsEnabled(ConvertLogLevel(logLevel));
        }

        IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;

        private static NLog.LogLevel ConvertLogLevel(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Trace => NLog.LogLevel.Trace,
            LogLevel.Debug => NLog.LogLevel.Debug,
            LogLevel.Information => NLog.LogLevel.Info,
            LogLevel.Warning => NLog.LogLevel.Warn,
            LogLevel.Error => NLog.LogLevel.Error,
            LogLevel.Critical => NLog.LogLevel.Fatal,
            _ => NLog.LogLevel.Off,
        };

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    public static partial class CustomLoggerExtensions
    {
        extension(ILoggerFactory factory)
        {
            public ILoggerFactory AddMSLogger(IHttpContextAccessor accessor)
            {
                factory.AddProvider(new MSLoggerProvider(accessor));
                return factory;
            }
        }
    }
}
