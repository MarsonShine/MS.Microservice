using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Idempotency.EFCore;
using MS.Microservice.Reference.Persistence;

namespace MS.Microservice.Reference.Web;

internal sealed class ReferenceIdempotencyCleanupWorker(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<ReferenceIdempotencyCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ReferenceHttpIdempotencyExecutor.CleanupInterval, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var context = scope.ServiceProvider.GetRequiredService<ReferenceDbContext>();
                    if ((await context.Database.GetPendingMigrationsAsync(stoppingToken)).Any()) continue;

                    var store = scope.ServiceProvider.GetRequiredService<EfCoreIdempotencyStore<ReferenceDbContext>>();
                    await store.PruneExpiredAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Could not prune expired HTTP idempotency records.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown cancels the timer wait.
        }
    }
}
