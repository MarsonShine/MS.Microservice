using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MS.Microservice.Infrastructure.Messaging;

internal sealed class OutboxPublisherBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxPublisherOptions> options) : BackgroundService
{
    private readonly OutboxPublisherOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var publisher = scope.ServiceProvider.GetRequiredService<OutboxPublisher>();
            var publishedCount = await publisher.PublishBatchAsync(stoppingToken);
            if (publishedCount == 0)
            {
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
        }
    }
}
