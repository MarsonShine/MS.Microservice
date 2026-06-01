using Microsoft.Extensions.Logging;
using MS.Microservice.Web.Infrastructure.LogUtils.Nlog.Performance;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog
{
    /// <summary>
    /// 高性能请求日志中间件。
    /// 使用 <see cref="TimeProvider.GetTimestamp"/> / <see cref="TimeProvider.GetElapsedTime(long)"/>
    /// 进行高精度计时，既避免直接依赖系统时钟，也让单元测试可以注入假时钟控制时间。
    /// </summary>
    public sealed class MSLoggerMiddleware(RequestDelegate next, TimeProvider timeProvider, ILogger<MSLoggerMiddleware> logger)
    {
        private readonly RequestDelegate _next = next;
        private readonly TimeProvider _timeProvider = timeProvider;
        private readonly ILogger<MSLoggerMiddleware> _logger = logger;

        // HttpContext.Items 键名常量，供 LayoutRenderer 共用，避免魔法字符串分散
        internal const string ElapsedKey = "MSLog_ElapsedMs";
        internal const string RequestIdKey = "MSLog_RequestId";
        internal const string PlatformIdKey = "MSLog_PlatformId";
        internal const string UserFlagKey = "MSLog_UserFlag";

        public async Task InvokeAsync(HttpContext context)
        {
            var startTimestamp = _timeProvider.GetTimestamp();

            // 一次采集请求上下文，存入 Items 供所有 LayoutRenderer 零查询读取
            SnapshotRequestContext(context);

            try
            {
                await _next(context);
            }
            finally
            {
                var elapsedMs = (long)_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;
                // 预计算耗时，LayoutRenderer 直接读这个值，不再解析 TimeProvider
                context.Items[ElapsedKey] = elapsedMs;

                if (_logger.IsEnabled(LogLevel.Information))
                {
                    var status = context.Response?.StatusCode ?? 0;
                    var path = context.Request?.Path.Value ?? string.Empty;
                    var method = context.Request?.Method;

                    _logger.LogHttpRequest(method, path, status, elapsedMs);
                }
            }
        }

        private static void SnapshotRequestContext(HttpContext context)
        {
            var headers = context.Request.Headers;

            if (headers.TryGetValue("requestId", out var rid) && rid.Count > 0)
                context.Items[RequestIdKey] = rid[0]!;
            if (headers.TryGetValue("platformId", out var pid) && pid.Count > 0)
                context.Items[PlatformIdKey] = pid[0]!;
            if (headers.TryGetValue("userflag", out var uf) && uf.Count > 0)
                context.Items[UserFlagKey] = uf[0]!;
        }
    }

    public static partial class PlatformLoggingApplicationBuilderExtensions
    {
        extension(IApplicationBuilder builder)
        {
            /// <summary>
            /// 注册平台日志中间件，确保 NLog 在应用停止时安全关闭。
            /// 自动从 DI 解析 <see cref="TimeProvider"/>（未注册时回退到 <see cref="TimeProvider.System"/>）。
            /// </summary>
            public IApplicationBuilder UsePlatformLogger()
            {
                var lifetime = builder.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
                lifetime.ApplicationStopped.Register(NLog.LogManager.Shutdown);
                builder.UseMiddleware<MSLoggerMiddleware>();
                return builder;
            }
        }
    }
}