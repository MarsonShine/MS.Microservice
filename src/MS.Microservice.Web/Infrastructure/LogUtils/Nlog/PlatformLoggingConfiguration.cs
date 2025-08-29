using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Web;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog
{
    public class PlatformLoggingConfiguration
    {
        private readonly IHostBuilder _builder;

        public PlatformLoggingConfiguration(IHostBuilder builder)
        {
            _builder = builder;
        }

        public IHostBuilder NLogConfiguration(LogLevel? logLevel = null)
        {
            return _builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                if (logLevel.HasValue)
                {
                    logging.SetMinimumLevel(logLevel.Value);
                }
            }).UseNLog();
        }
    }
}
