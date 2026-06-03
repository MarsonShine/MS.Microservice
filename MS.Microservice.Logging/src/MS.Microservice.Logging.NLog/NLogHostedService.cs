using Microsoft.Extensions.Hosting;

namespace MS.Microservice.Logging.NLog;

internal sealed class NLogHostedService : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        global::NLog.LogManager.Shutdown();
        return Task.CompletedTask;
    }
}