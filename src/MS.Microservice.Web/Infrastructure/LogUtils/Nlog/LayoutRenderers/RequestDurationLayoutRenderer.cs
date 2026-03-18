using NLog;
using NLog.LayoutRenderers;
using NLog.Web.LayoutRenderers;
using System;
using System.Text;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog.LayoutRenderers
{
    /// <summary>
    /// 高精度请求耗时渲染器。
    /// 通过 <see cref="TimeProvider"/> 抽象计时，使其在单元测试中可被替换（注入 FakeTimeProvider）。
    /// 生产环境默认使用 <see cref="TimeProvider.System"/>。
    /// </summary>
    [LayoutRenderer("aspnet-request-duration")]
    public sealed class RequestDurationLayoutRenderer : AspNetLayoutRendererBase
    {
        /// <summary>
        /// 可由 DI 启动时赋值（在 MSLoggerBuilder.WithNLogger 中设置）。
        /// 默认使用系统时钟；测试时注入 FakeTimeProvider。
        /// </summary>
        public static TimeProvider TimeProvider { get; set; } = TimeProvider.System;

        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            var context = HttpContextAccessor?.HttpContext;
            if (context is null)
                return;

            if (context.Items.TryGetValue("ElapsedTime", out var val) && val is long startTimestamp)
            {
                var elapsed = TimeProvider.GetElapsedTime(startTimestamp);
                var ms = (long)elapsed.TotalMilliseconds;
                builder.Append(ms);
                builder.Append("ms");
            }
        }
    }
}
