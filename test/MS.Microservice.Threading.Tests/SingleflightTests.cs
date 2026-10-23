using System.Collections.Concurrent;
using System.Threading.Channels;
using Xunit;

namespace MS.Microservice.Threading.Tests;

public class SingleflightTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ConcurrentCallersShareOnlyTheInFlightResult()
    {
        var manager = new SingleflightManager();
        var release = Completion<int>();
        var allJoined = Completion<bool>();
        int executions = 0;
        int joined = 0;
        Task<int> Execute() { Interlocked.Increment(ref executions); return release.Task; }

        var calls = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(async () =>
            {
                var pending = manager.ExecuteOnceAsync("key", Execute);
                if (Interlocked.Increment(ref joined) == 20)
                    allJoined.SetResult(true);
                return await pending;
            })).ToArray();
        try
        {
            await allJoined.Task.WaitAsync(TestTimeout);
            Assert.Equal(1, executions);
            Assert.All(calls, call => Assert.False(call.IsCompleted));
        }
        finally { release.TrySetResult(42); }

        Assert.All(await Task.WhenAll(calls).WaitAsync(TestTimeout), value => Assert.Equal(42, value));
        Assert.Equal(43, await manager.ExecuteOnceAsync("key", () => Task.FromResult(43)).WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task FaultIsSharedAndTheNextCallCanRetry()
    {
        var manager = new SingleflightManager();
        var release = Completion<int>();
        var error = new IOException("shared failure");
        var first = manager.ExecuteOnceAsync("key", () => release.Task);
        var second = manager.ExecuteOnceAsync("key", () => Task.FromResult(-1));

        release.SetException(error);
        Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => first.WaitAsync(TestTimeout)));
        Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => second.WaitAsync(TestTimeout)));
        Assert.Equal(7, await manager.ExecuteOnceAsync("key", () => Task.FromResult(7)).WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task SynchronousFactoryFailureDoesNotLeaveAnInFlightEntry()
    {
        var manager = new SingleflightManager();
        var error = new IOException("synchronous failure");

        Assert.Same(error, await Assert.ThrowsAsync<IOException>(async () =>
            await manager.ExecuteOnceAsync<int>("key", () => throw error).WaitAsync(TestTimeout)));
        Assert.Equal(7, await manager.ExecuteOnceAsync("key", () => Task.FromResult(7)).WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task OperationCancellationIsSharedAndTheNextCallCanRetry()
    {
        var manager = new SingleflightManager();
        var release = Completion<int>();
        using var operationStop = new CancellationTokenSource();
        var first = manager.ExecuteOnceAsync("key", () => release.Task);
        var second = manager.ExecuteOnceAsync("key", () => Task.FromResult(-1));

        operationStop.Cancel();
        release.SetCanceled(operationStop.Token);
        var firstError = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TestTimeout));
        var secondError = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(TestTimeout));
        Assert.Equal(operationStop.Token, firstError.CancellationToken);
        Assert.Equal(operationStop.Token, secondError.CancellationToken);
        Assert.True(first.IsCanceled);
        Assert.True(second.IsCanceled);
        Assert.Equal(7, await manager.ExecuteOnceAsync("key", () => Task.FromResult(7)).WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task ResultTypesAndKeysAreIndependent()
    {
        var manager = new SingleflightManager();
        var number = Completion<int>();
        var text = Completion<string>();
        var other = Completion<int>();
        var entered = new ConcurrentBag<string>();
        var first = manager.ExecuteOnceAsync("key", () => { entered.Add("number"); return number.Task; });
        var second = manager.ExecuteOnceAsync("key", () => { entered.Add("text"); return text.Task; });
        var third = manager.ExecuteOnceAsync("other", () => { entered.Add("other"); return other.Task; });

        try { Assert.Equal(3, entered.Count); }
        finally
        {
            number.TrySetResult(1);
            text.TrySetResult("value");
            other.TrySetResult(2);
        }

        Assert.Equal(1, await first.WaitAsync(TestTimeout));
        Assert.Equal("value", await second.WaitAsync(TestTimeout));
        Assert.Equal(2, await third.WaitAsync(TestTimeout));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelingOneCallerDoesNotCancelOrRemoveSharedOperation(bool cancelFirstCaller)
    {
        var manager = new SingleflightManager();
        var release = Completion<int>();
        using var stop = new CancellationTokenSource();
        int executions = 0;
        Task<int> Execute() { Interlocked.Increment(ref executions); return release.Task; }

        var first = manager.ExecuteOnceAsync("key", Execute, cancelFirstCaller ? stop.Token : default);
        var second = manager.ExecuteOnceAsync("key", Execute, cancelFirstCaller ? default : stop.Token);
        var canceled = cancelFirstCaller ? first : second;
        var survivor = cancelFirstCaller ? second : first;
        Task<int>? lateCaller = null;
        try
        {
            stop.Cancel();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.WaitAsync(TestTimeout));
            Assert.Equal(stop.Token, error.CancellationToken);
            Assert.False(survivor.IsCompleted);
            lateCaller = manager.ExecuteOnceAsync("key", Execute);
            Assert.Equal(1, executions);
            Assert.False(lateCaller.IsCompleted);
        }
        finally { release.TrySetResult(42); }

        Assert.Equal(42, await survivor.WaitAsync(TestTimeout));
        Assert.Equal(42, await lateCaller!.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task LateCompletionOfOldWaiterCannotRemoveNewInFlightOperation()
    {
        var manager = new SingleflightManager();
        var oldRelease = Completion<int>();
        var newRelease = Completion<int>();
        var oldOwner = manager.ExecuteOnceAsync("key", () => oldRelease.Task);
        var delayedContext = new QueuedContext();
        Task<int> oldWaiter;
        var previousContext = SynchronizationContext.Current;
        try
        {
            // A context-capturing implementation must not let this old waiter's
            // delayed cleanup delete the operation started in the next generation.
            SynchronizationContext.SetSynchronizationContext(delayedContext);
            oldWaiter = manager.ExecuteOnceAsync("key", () => Task.FromResult(-1));
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }

        oldRelease.SetResult(1);
        Assert.Equal(1, await oldOwner.WaitAsync(TestTimeout));
        var newOwner = manager.ExecuteOnceAsync("key", () => newRelease.Task);
        Task<int>? newWaiter = null;
        bool duplicateExecuted = false;
        try
        {
            await delayedContext.RunUntilCompleteAsync(oldWaiter).WaitAsync(TestTimeout);
            Assert.Equal(1, await oldWaiter);
            newWaiter = manager.ExecuteOnceAsync("key", () =>
            {
                duplicateExecuted = true;
                return Task.FromResult(-1);
            });
            Assert.False(duplicateExecuted);
            Assert.False(newWaiter.IsCompleted);
        }
        finally { newRelease.TrySetResult(2); }

        Assert.Equal(2, await newOwner.WaitAsync(TestTimeout));
        Assert.Equal(2, await newWaiter!.WaitAsync(TestTimeout));
    }

    private static TaskCompletionSource<T> Completion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Channel<(SendOrPostCallback Callback, object? State)> _callbacks =
            Channel.CreateUnbounded<(SendOrPostCallback, object?)>();

        public override void Post(SendOrPostCallback callback, object? state) =>
            _callbacks.Writer.TryWrite((callback, state));

        public async Task RunUntilCompleteAsync(Task task)
        {
            while (!task.IsCompleted)
            {
                await Task.WhenAny(task, _callbacks.Reader.WaitToReadAsync().AsTask());
                while (_callbacks.Reader.TryRead(out var work))
                    work.Callback(work.State);
            }
        }
    }
}
