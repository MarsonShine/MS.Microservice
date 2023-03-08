using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;
using Microsoft.Extensions.DependencyInjection;

namespace MS.Microservice.Web.Application.BackgroundServices
{
    public class BadPracticeBackgroundService : BackgroundService
    {
        private Timer _timer;
        private Queue<Func<CancellationToken, ValueTask>> _queue;
        private volatile int running = 0;
        private const int size = 5000;
        private const int queueSize = 100;
        private readonly ILogger<BadPracticeBackgroundService> _logger;

        public BadPracticeBackgroundService(
            IServiceProvider service,
            ILogger<BadPracticeBackgroundService> logger)
        {
            Services = service;
            _logger = logger;
        }

        public IServiceProvider Services { get; }
        public Queue<Func<CancellationToken, ValueTask>> Queue => _queue;

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            // TODO：后台作业待配置化
            _timer = new Timer(DoWork, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            _queue = new Queue<Func<CancellationToken, ValueTask>>(queueSize);

            return base.StartAsync(cancellationToken);
        }

        private void DoWork(object state)
        {
            ExecuteAsync(CancellationToken.None).ContinueWith(_ => { });
        }

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
        private async ValueTask DoWorkAsync(CancellationToken cancellationToken = default)
        {
            if (TryAccquired())
            {
                if (Queue.Count > 0)
                {
                    var worker = Queue.Dequeue();
                    await worker(cancellationToken);
                }
                else
                {
                    try
                    {
                        await DoWorkFromQueueAsync(cancellationToken);
                    }
                    finally
                    {
                        // 防止异常死锁
                        Release();
                    }

                }
                Release();
                return;
            }
            if (_queue.Count == queueSize)
            {
                // 拒绝
                return;
            }
            _queue.Enqueue(cancel => DoWorkFromQueueAsync(cancel));
        }

        private async ValueTask DoWorkFromQueueAsync(CancellationToken cancellationToken = default)
        {
            using var scope = Services.CreateScope();
            // do something
            await Task.CompletedTask;
        }

        private bool TryAccquired() => Interlocked.CompareExchange(ref running, 1, 0) == 0;

        private void Release() => Interlocked.Exchange(ref running, 0);
    }
}
