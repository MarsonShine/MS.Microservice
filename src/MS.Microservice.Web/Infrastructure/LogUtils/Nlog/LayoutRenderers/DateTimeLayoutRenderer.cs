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
    /// 从 HttpContext 请求头中提取值的渲染器，需要 AspNetLayoutRendererBase。
    /// 使用 StringValues 的隐式转换避免 ToString() 分配（当 header 值已经是单个字符串时）。
    /// </summary>
    [LayoutRenderer("requestId")]
    public sealed class RequestIdLayoutRenderer : AspNetLayoutRendererBase
    {
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            var context = HttpContextAccessor?.HttpContext;
            if (context is not null &&
                context.Request.Headers.TryGetValue("requestId", out var values) &&
                values.Count > 0)
            {
                builder.Append(values[0]);
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
                context.Request.Headers.TryGetValue("platformId", out var values) &&
                values.Count > 0)
            {
                builder.Append(values[0]);
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
                context.Request.Headers.TryGetValue("userflag", out var values) &&
                values.Count > 0)
            {
                builder.Append(values[0]);
            }
        }
    }
}
