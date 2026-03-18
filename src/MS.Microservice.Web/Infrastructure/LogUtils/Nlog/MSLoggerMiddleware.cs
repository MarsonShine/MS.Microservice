using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using System;
using System.Threading.Tasks;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog
{
    /// <summary>
    /// 高性能请求日志中间件。
    /// 使用 <see cref="TimeProvider.GetTimestamp"/> / <see cref="TimeProvider.GetElapsedTime"/>
    /// 进行高精度计时，既避免直接依赖系统时钟，也让单元测试可以注入假时钟控制时间。
    /// </summary>
    public sealed class MSLoggerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly TimeProvider _timeProvider;
        private static readonly Logger NLogger = LogManager.GetCurrentClassLogger();

        public MSLoggerMiddleware(RequestDelegate next, TimeProvider timeProvider)
        {
            _next = next;
            _timeProvider = timeProvider;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // GetTimestamp() 基于高精度计时器（与 Stopwatch 使用同一底层 API）
            var startTimestamp = _timeProvider.GetTimestamp();

            // 存入 HttpContext.Items 供 LayoutRenderer 读取（long 装箱一次，不可避免）
            context.Items["ElapsedTime"] = startTimestamp;

            try
            {
                await _next(context);
            }
            finally
            {
                if (NLogger.IsInfoEnabled)
                {
                    // GetElapsedTime 同样经过 TimeProvider 抽象，测试中可被控制
                    var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
                    var elapsedMs = (long)elapsed.TotalMilliseconds;
                    var status = context.Response?.StatusCode ?? 0;
                    var path = context.Request?.Path.Value ?? string.Empty;
                    var method = context.Request?.Method;

                    NLogger.WithProperty("elapsedTime", elapsedMs)
                           .Info("HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs}ms",
                                 method, path, status, elapsedMs);
                }
            }
        }
    }

    public static class PlatformLoggingApplicationBuilderExtensions
    {
        /// <summary>
        /// 注册平台日志中间件，确保 NLog 在应用停止时安全关闭。
        /// 自动从 DI 解析 <see cref="TimeProvider"/>（未注册时回退到 <see cref="TimeProvider.System"/>）。
        /// </summary>
        public static IApplicationBuilder UsePlatformLogger(this IApplicationBuilder builder)
        {
            var lifetime = builder.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStopped.Register(LogManager.Shutdown);
            builder.UseMiddleware<MSLoggerMiddleware>();
            return builder;
        }
    }
}