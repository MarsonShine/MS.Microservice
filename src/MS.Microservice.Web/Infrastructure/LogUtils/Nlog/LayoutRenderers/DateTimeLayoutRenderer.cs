using NLog;
using NLog.LayoutRenderers;
using NLog.Web.LayoutRenderers;
using System;
using System.Text;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog.LayoutRenderers
{
    [LayoutRenderer("hours")]
    public class HoursLayoutRenderer : AspNetLayoutRendererBase
    {
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            builder.Append(DateTimeOffset.Now.Hour);
        }
    }

    [LayoutRenderer("year")]
    public class YearLayoutRenderer : AspNetLayoutRendererBase
    {
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            builder.Append(DateTimeOffset.Now.Year);
        }
    }

    [LayoutRenderer("month")]
    public class MonthLayoutRenderer : AspNetLayoutRendererBase
    {
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            builder.Append(DateTimeOffset.Now.Month);
        }
    }

    // Static holders to supply values without obsolete AspNetLayoutRendererBase.Register
    [LayoutRenderer("NetAddress")]
    public class NetAddressLayoutRenderer : AspNetLayoutRendererBase
    {
        public static string? Value { get; set; }
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            if (Value is not null) builder.Append(Value);
        }
    }

    [LayoutRenderer("LogLevel")]
    public class LogLevelLayoutRenderer : AspNetLayoutRendererBase
    {
        public static string? Value { get; set; }
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            if (Value is not null) builder.Append(Value);
        }
    }

    [LayoutRenderer("requestId")]
    public class RequestIdLayoutRenderer : AspNetLayoutRendererBase
    {
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            var context = HttpContextAccessor?.HttpContext;
            var value = context?.Request?.Headers["requestId"].ToString();
            if (!string.IsNullOrEmpty(value)) builder.Append(value);
        }
    }

    [LayoutRenderer("platformId")]
    public class PlatformIdLayoutRenderer : AspNetLayoutRendererBase
    {
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            var context = HttpContextAccessor?.HttpContext;
            var value = context?.Request?.Headers["platformId"].ToString();
            if (!string.IsNullOrEmpty(value)) builder.Append(value);
        }
    }

    [LayoutRenderer("userflag")]
    public class UserFlagLayoutRenderer : AspNetLayoutRendererBase
    {
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            var context = HttpContextAccessor?.HttpContext;
            var value = context?.Request?.Headers["userflag"].ToString();
            if (!string.IsNullOrEmpty(value)) builder.Append(value);
        }
    }
}
