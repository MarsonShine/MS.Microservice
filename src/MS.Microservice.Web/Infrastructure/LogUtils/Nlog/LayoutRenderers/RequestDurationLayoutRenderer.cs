using NLog;
using NLog.LayoutRenderers;
using NLog.Web.LayoutRenderers;
using System;
using System.Text;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog.LayoutRenderers
{
    /// <summary>
    /// 高精度请求耗时渲染器。
    /// 读取 <see cref="MSLoggerMiddleware"/> 预计算的耗时值（HttpContext.Items），
    /// 避免每次日志事件解析 TimeProvider。
    /// </summary>
    [LayoutRenderer("aspnet-request-duration")]
    public sealed class RequestDurationLayoutRenderer : AspNetLayoutRendererBase
    {
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            var context = HttpContextAccessor?.HttpContext;
            if (context is null)
                return;

            if (context.Items.TryGetValue(MSLoggerMiddleware.ElapsedKey, out var val) && val is long elapsedMs)
            {
                builder.Append(elapsedMs);
                builder.Append("ms");
            }
        }
    }
}
