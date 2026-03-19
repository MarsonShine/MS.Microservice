using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Web;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog
{
    /// <summary>
    /// 封装 NLog 与 Microsoft.Extensions.Logging 管道的集成配置。
    /// 提供两套构造函数：
    ///   1. <see cref="IHostBuilder"/>（传统 GenericHost，通过 builder.Host 获取）
    ///   2. <see cref="IHostApplicationBuilder"/>（.NET 8+ WebApplicationBuilder，推荐新项目使用）
    /// </summary>
    public sealed class PlatformLoggingConfiguration
    {
        private readonly IHostBuilder? _hostBuilder;
        private readonly IHostApplicationBuilder? _appBuilder;

        public PlatformLoggingConfiguration(IHostBuilder builder)
        {
            _hostBuilder = builder;
        }

        /// <summary>
        /// .NET 8+ 推荐入口：通过 <see cref="IHostApplicationBuilder"/> 配置（如 WebApplicationBuilder）。
        /// </summary>
        public PlatformLoggingConfiguration(IHostApplicationBuilder builder)
        {
            _appBuilder = builder;
        }

        /// <summary>
        /// 清除默认 providers，接管为 NLog，并可选地设置最低日志级别。
        /// </summary>
        public void NLogConfiguration(LogLevel? logLevel = null)
        {
            if (_appBuilder is not null)
            {
                // IHostApplicationBuilder 路径（WebApplicationBuilder / HostApplicationBuilder）
                _appBuilder.Logging.ClearProviders();
                if (logLevel.HasValue)
                    _appBuilder.Logging.SetMinimumLevel(logLevel.Value);

                // UseNLog() 通过扩展方法同样支持 IHostApplicationBuilder
                _appBuilder.UseNLog();
            }
            else
            {
                // IHostBuilder 路径（builder.Host）——向后兼容
                _hostBuilder?.ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    if (logLevel.HasValue)
                        logging.SetMinimumLevel(logLevel.Value);
                }).UseNLog();
            }
        }
    }
}
