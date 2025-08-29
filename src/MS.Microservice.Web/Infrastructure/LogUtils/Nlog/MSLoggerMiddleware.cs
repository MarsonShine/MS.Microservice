using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog
{
    /// <summary>
    /// 封装错误信息，自动发送给日志服务器
    /// 需要定义的格式为：layout={#${longdate}#${nodeName}#${logger}#${uppercase:${level}}#${callsite}#${callsite-linenumber}#${aspnet-request-url}#${aspnet-request-method}#${aspnet-mvc-controller}#${aspnet-mvc-action}#${message}#${exception:format=ToString}#${elapsedTime}#}
    /// </summary>
    public class MSLoggerMiddleware
    {
        public readonly RequestDelegate _next;
        private readonly Logger _logger;
        public MSLoggerMiddleware(RequestDelegate next, ILoggerFactory loggerFactory, IHttpContextAccessor accessor, IHostApplicationLifetime lifetime)
        {
            _next = next;
            loggerFactory.AddMSLogger(accessor);
            _logger = LogManager.GetLogger(nameof(MSLoggerMiddleware));
            SafeStopLogger(lifetime);
        }

        private static void SafeStopLogger(IHostApplicationLifetime lifetime)
        {
            lifetime.ApplicationStopped.Register(LogManager.Shutdown);
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            context.Items.TryAdd("ElapsedTime", DateTimeOffset.Now.Ticks);
            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();
                // Avoid allocating formatted string when level disabled
                if (_logger.IsInfoEnabled)
                {
                    var status = context.Response?.StatusCode ?? 0;
                    var path = context.Request?.Path.Value ?? string.Empty;
                    _logger.WithProperty("elapsedTime", stopwatch.ElapsedMilliseconds + "ms")
                           .Info("HTTP {Method} {Path} -> {Status}", context.Request?.Method, path, status);
                }
            }
        }
    }

    public static class PlatformLoggingApplicationBuilderExtensions
    {
        public static void UsePlatformLogger(this IApplicationBuilder builder)
        {
            var lifetime = builder.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStopped.Register(LogManager.Shutdown);
            builder.UseMiddleware<MSLoggerMiddleware>();
        }
    }
}