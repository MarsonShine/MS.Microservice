using NLog;
using NLog.LayoutRenderers;
using NLog.Web.LayoutRenderers;
using System;
using System.Text;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog.LayoutRenderers
{
    [LayoutRenderer("aspnet-request-duration")]
    public class RequestDurationLayoutRenderer : AspNetLayoutRendererBase
    {
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            var context = HttpContextAccessor?.HttpContext;
            if (context is null)
                return;

            if (context.Items.TryGetValue("ElapsedTime", out var val) && val is long ticks)
            {
                var timespan = new TimeSpan(DateTimeOffset.Now.Ticks - ticks);
                if (timespan != TimeSpan.Zero)
                {
                    builder.Append(timespan.TotalMilliseconds).Append("ms");
                }
            }
        }
    }
}
