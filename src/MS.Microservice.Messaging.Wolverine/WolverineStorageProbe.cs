using global::Wolverine.Runtime;

namespace MS.Microservice.Messaging.Wolverine;

internal sealed class WolverineStorageProbe(IWolverineRuntime runtime) : IMessageStorageProbe
{
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        await runtime.Storage.Admin.CheckConnectivityAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await runtime.Storage.Admin.FetchCountsAsync();
        cancellationToken.ThrowIfCancellationRequested();
    }
}
