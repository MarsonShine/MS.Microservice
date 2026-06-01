using NLog;
using NLog.LayoutRenderers;
using NLog.Web.LayoutRenderers;
using System;
using System.Globalization;
using System.Text;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog.LayoutRenderers
{
    /// <summary>
    /// 时间相关的 LayoutRenderer 继承 LayoutRenderer（而非 AspNetLayoutRendererBase），
    /// 因为它们不需要 HttpContext，避免无意义的 IHttpContextAccessor 查找。
    /// </summary>
    [LayoutRenderer("hours")]
    public sealed class HoursLayoutRenderer : LayoutRenderer
    {
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            // 使用 logEvent.TimeStamp 而非 DateTimeOffset.Now，保证与日志事件时间一致
            builder.Append(logEvent.TimeStamp.Hour);
        }
    }

    [LayoutRenderer("year")]
    public sealed class YearLayoutRenderer : LayoutRenderer
    {
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            builder.Append(logEvent.TimeStamp.Year);
        }
    }

    [LayoutRenderer("month")]
    public sealed class MonthLayoutRenderer : LayoutRenderer
    {
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            builder.Append(logEvent.TimeStamp.Month);
        }
    }

    /// <summary>
    /// 静态配置值渲染器，不需要 HttpContext。
    /// </summary>
    [LayoutRenderer("NetAddress")]
    public sealed class NetAddressLayoutRenderer : LayoutRenderer
    {
        public static string? Value { get; set; }

        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            if (Value is not null) builder.Append(Value);
        }
    }

    [LayoutRenderer("LogLevel")]
    public sealed class LogLevelLayoutRenderer : LayoutRenderer
    {
        public static string? Value { get; set; }

        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            if (Value is not null) builder.Append(Value);
        }
    }

    /// <summary>
    /// 从 <see cref="MSLoggerMiddleware"/> 预采集的请求上下文中读取 header 值。
    /// 中间件在请求入口一次性提取，后续渲染器零查询读取，避免每条日志事件重复查 Headers 字典。
    /// </summary>
    [LayoutRenderer("requestId")]
    public sealed class RequestIdLayoutRenderer : AspNetLayoutRendererBase
    {
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            var context = HttpContextAccessor?.HttpContext;
            if (context is not null &&
                context.Items.TryGetValue(MSLoggerMiddleware.RequestIdKey, out var val) &&
                val is string value)
            {
                builder.Append(value);
            }
        }
    }

    [LayoutRenderer("platformId")]
    public sealed class PlatformIdLayoutRenderer : AspNetLayoutRendererBase
    {
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            var context = HttpContextAccessor?.HttpContext;
            if (context is not null &&
                context.Items.TryGetValue(MSLoggerMiddleware.PlatformIdKey, out var val) &&
                val is string value)
            {
                builder.Append(value);
            }
        }
    }

    [LayoutRenderer("userflag")]
    public sealed class UserFlagLayoutRenderer : AspNetLayoutRendererBase
    {
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            var context = HttpContextAccessor?.HttpContext;
            if (context is not null &&
                context.Items.TryGetValue(MSLoggerMiddleware.UserFlagKey, out var val) &&
                val is string value)
            {
                builder.Append(value);
            }
        }
    }
}
