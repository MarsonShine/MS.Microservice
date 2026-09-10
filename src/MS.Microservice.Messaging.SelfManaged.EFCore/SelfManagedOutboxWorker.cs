using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MS.Microservice.Messaging.SelfManaged;

internal sealed class SelfManagedOutboxWorker<TContext>(IServiceScopeFactory scopeFactory,
    SelfManagedOptions options, TimeProvider clock, ILogger<SelfManagedOutboxWorker<TContext>> logger)
    : BackgroundService where TContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var count = await scope.ServiceProvider.GetRequiredService<SelfManagedPublisher<TContext>>()
                    .PublishBatchAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<OutboxStore<TContext>>().CleanupAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<InboxStore<TContext>>().CleanupAsync(stoppingToken);
                failures = 0;
                if (count == 0) await Task.Delay(options.PollInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) when (exception is DbException or DbUpdateException or TimeoutException)
            {
                logger.LogWarning("Messaging storage unavailable: {ErrorType}", exception.GetType().Name);
                try { await Task.Delay(options.RetryDelay(++failures), clock, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
