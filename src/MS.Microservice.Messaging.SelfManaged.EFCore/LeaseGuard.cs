namespace MS.Microservice.Messaging.SelfManaged;

/// <summary>Renews ownership separately from the business context and cancels work when ownership is lost.</summary>
internal sealed class LeaseGuard : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationTokenSource _work;
    private readonly Task _renewal;
    public CancellationToken Token => _work.Token;
    public bool Lost { get; private set; }

    public LeaseGuard(Func<CancellationToken, Task<bool>> renew, TimeSpan lease, TimeSpan timeout,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        _work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _work.CancelAfter(timeout);
        _renewal = RenewAsync(renew, TimeSpan.FromTicks(lease.Ticks / 3), clock);
    }

    private async Task RenewAsync(Func<CancellationToken, Task<bool>> renew, TimeSpan interval, TimeProvider clock)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await Task.Delay(interval, clock, _stop.Token);
                if (!await renew(_stop.Token))
                {
                    Lost = true;
                    await _work.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch
        {
            Lost = true;
            await _work.CancelAsync();
        }
    }

    public async Task StopRenewingAsync()
    {
        await _stop.CancelAsync();
        await _renewal;
    }

    public async ValueTask DisposeAsync()
    {
        await StopRenewingAsync();
        _stop.Dispose();
        _work.Dispose();
    }
}
