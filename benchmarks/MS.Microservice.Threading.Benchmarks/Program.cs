using MS.Microservice.Threading;

foreach (int size in new[] { 0, 1, 512 })
{
    var items = Enumerable.Range(0, size).ToArray();
    MeasuredOperation.Run($"Batch / {size} ready items", size < 2 ? 256 : 128,
        () => BatchProcessing.CollectAsync<int, int>(items, static (item, _) => ValueTask.FromResult(item * 2), 4)
            .GetAwaiter().GetResult(),
        results => Correct(results, size));
}

int[] yielding = Enumerable.Range(0, 64).ToArray();
MeasuredOperation.Run("Batch / 64 yielding items", 64,
    () => BatchProcessing.CollectAsync<int, int>(yielding, static async (item, _) =>
    {
        await Task.Yield();
        return item * 2;
    }, 4).GetAwaiter().GetResult(),
    results => Correct(results, yielding.Length));

var manager = new SingleflightManager();
var ready = Task.FromResult(42);
Func<Task<int>> completed = () => ready;
MeasuredOperation.Run("Singleflight / one ready owner", 4096,
    () => manager.ExecuteOnceAsync("key", completed).GetAwaiter().GetResult(), value => value == 42);
MeasuredOperation.Run("Singleflight / owner and 64 followers", 64,
    () => Share(cancelWaiter: false), correct => correct);
using var callerLifetime = new CancellationTokenSource();
MeasuredOperation.Run("Singleflight / owner and 64 cancellable followers", 64,
    () => Share(cancelWaiter: false, callerLifetime.Token), correct => correct);
MeasuredOperation.Run("Singleflight / owner and 64 followers with one cancellation", 64,
    () => Share(cancelWaiter: true), correct => correct);

bool Share(bool cancelWaiter, CancellationToken followerToken = default)
{
    var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    var owner = manager.ExecuteOnceAsync("key", () => gate.Task);
    using var cancellation = cancelWaiter ? new CancellationTokenSource() : null;
    var followers = new Task<int>[64];
    for (int i = 0; i < followers.Length; i++)
    {
        var token = i == 0 && cancellation is not null ? cancellation.Token : followerToken;
        followers[i] = manager.ExecuteOnceAsync<int>("key", static () => throw new InvalidOperationException("Duplicate factory."), token);
    }
    if (cancellation is not null)
    {
        cancellation.Cancel();
        ((Task)followers[0]).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing).GetAwaiter().GetResult();
    }
    gate.SetResult(42);
    bool correct = owner.GetAwaiter().GetResult() == 42;
    for (int i = 0; i < followers.Length; i++)
    {
        if (i == 0 && cancelWaiter)
        {
            correct &= followers[i].IsCanceled;
        }
        else
        {
            correct &= followers[i].GetAwaiter().GetResult() == 42;
        }
    }
    return correct;
}

static bool Correct(IReadOnlyList<BatchItemResult<int>> results, int count) =>
    results.Count == count && results.Select((item, i) => item.Index == i && item.IsSuccess && item.Value == i * 2).All(valid => valid);
