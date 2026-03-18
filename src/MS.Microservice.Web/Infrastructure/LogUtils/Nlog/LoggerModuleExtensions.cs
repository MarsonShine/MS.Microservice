using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog
{
    public static class LoggerModuleExtensions
    {
        /// <summary>
        /// 注册 MS 日志服务，使用 TryAddSingleton 避免重复注册。
        /// 同时注册 <see cref="TimeProvider"/> 以供中间件和实体使用（可在测试中替换）。
        /// </summary>
        public static MSLoggerBuilder AddMSLoggerService(this IServiceCollection services)
        {
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            // ASP.NET Core 并不自动将 TimeProvider.System 注入 DI，需显式注册
            services.TryAddSingleton(TimeProvider.System);
            return new MSLoggerBuilder(services);
        }
    }

    public static class LoggerHostBuilderExtensions
    {
        /// <summary>
        /// 通过 <see cref="IHostBuilder"/>（builder.Host）配置平台日志——向后兼容路径。
        /// </summary>
        public static IHostBuilder ConfigurePlatformLogging(
            this IHostBuilder hostBuilder,
            Action<PlatformLoggingConfiguration> configure)
        {
            var configuration = new PlatformLoggingConfiguration(hostBuilder);
            configure(configuration);
            return hostBuilder;
        }

        /// <summary>
        /// 通过 <see cref="IHostApplicationBuilder"/>（WebApplicationBuilder）配置平台日志——.NET 8+ 推荐路径。
        /// </summary>
        public static IHostApplicationBuilder ConfigurePlatformLogging(
            this IHostApplicationBuilder appBuilder,
            Action<PlatformLoggingConfiguration> configure)
        {
            var configuration = new PlatformLoggingConfiguration(appBuilder);
            configure(configuration);
            return appBuilder;
        }
    }
}