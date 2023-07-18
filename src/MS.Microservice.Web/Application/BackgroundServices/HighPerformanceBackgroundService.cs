using System.Threading;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Domain.Services.Interfaces;

namespace MS.Microservice.Web.Application.BackgroundServices
{
    /// <summary>
    /// 后台作业运行超时时间为5秒，如果想手动设置超时时间，请再<see cref="Program"/>设置：WebHost.CreateDefaultBuilder(args).UseShutdownTimeout(TimeSpan.FromSeconds(10))
    /// 注意，此后台作业可能会根据业务量发生大量的任务作业
    /// </summary>
    public class HighPerformanceBackgroundService: BackgroundService
    {
        private readonly Timer _timer;
        private readonly Channel<Func<CancellationToken, ValueTask>> _queue;
        private const int queueSize = 100;
        private readonly ILogger<HighPerformanceBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly CancellationToken _cancellationToken;

        public HighPerformanceBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<HighPerformanceBackgroundService> logger,
            IHostApplicationLifetime applicationLifetime)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _cancellationToken = applicationLifetime.ApplicationStopping;
            BoundedChannelOptions options = new(queueSize)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
            };
            _queue = Channel.CreateBounded<Func<CancellationToken, ValueTask>>(options);
#if DEBUG
            _timer = new Timer(DoWork!, null, TimeSpan.Zero, TimeSpan.FromSeconds(60 * 5));
#else
_timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
#endif
        }
        private void DoWork(object state)
        {
            DoWorkAsync(_cancellationToken).ContinueWith(_ => { });
        }
        private async Task DoWorkAsync(CancellationToken stoppingToken)
        {
            if (_queue.Reader.TryRead(out var worker))
            {
                await worker(stoppingToken);
                return;
            }
            await _queue.Writer.WriteAsync(DoWorkFromQueueAsync, stoppingToken);
        }
        private async ValueTask DoWorkFromQueueAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IUserDomainService>();
            try
            {
                await service.FindAsync("", cancellationToken);
                // do something...
            }
            catch (Exception ex)
            {
                _logger.LogError("激活码系统调用失败：{Message}", ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }
        // 为何要用Task.Run，是为了防止再程序启动一个长时间阻塞的作业，导致整个应用程序启动阻塞。
        // 详见：https://blog.stephencleary.com/2020/05/backgroundservice-gotcha-startup.html
        // 官方团队可能会优化这点，具体详见：https://github.com/dotnet/runtime/issues/36063
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(async () =>
        {
            try
            {
                await DoWorkAsync(stoppingToken);
            }
            catch (Exception ex) when (False(() => _logger.LogError(ex, "激活码生成作业报错")))
            {
                throw;
            }
        }, stoppingToken);

        private static bool False(Action action)
        {
            action();
            return false;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, 0);
            return base.StopAsync(cancellationToken);
        }
    }
}
