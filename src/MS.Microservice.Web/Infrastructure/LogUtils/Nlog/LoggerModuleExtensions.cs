using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog
{
    public static class LoggerModuleExtensions
    {
        public static MSLoggerBuilder AddMSLoggerService(this IServiceCollection services)
        {
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            return new MSLoggerBuilder(services);
        }
    }

    public static class LoggerHostBuilderExtensions
    {
        public static IHostBuilder ConfigurePlatformLogging(this IHostBuilder hostBuilder, Action<PlatformLoggingConfiguration> configure)
        {
            var configuration = new PlatformLoggingConfiguration(hostBuilder);
            configure(configuration);
            return hostBuilder;
        }
    }
}