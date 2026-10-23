using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MS.Microservice.Core.Common.Advance.Resilience;
using MS.Microservice.Core.Common.Advance.Resilience.RetryCondition;
using MS.Microservice.Core.Common.Advance.Resilience.RetryStrategy;

namespace MS.Microservice.Core.Tests.Common.Advance.Resilience;

public class RetryExecutionContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCancelledCaller_DoesNotInvokeEitherOldAsyncEntry(bool returnsValue)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var attempts = 0;
        var executor = CreateExecutor();

        Task execution = returnsValue
            ? executor.ExecuteAsync(() => { attempts++; return Task.FromResult(42); }, cancellation.Token)
            : executor.ExecuteAsync(() => { attempts++; return Task.CompletedTask; }, cancellation.Token);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancelledByOperation_StopsEvenWithZeroDelay(bool operationThrows)
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var executor = CreateExecutor();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(() =>
        {
            attempts++;
            cancellation.Cancel();
            if (operationThrows)
                throw new InvalidOperationException("operation failed after cancellation");
            return Task.FromResult(42);
        }, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task CallerCancelledDuringDelay_DoesNotStartAnotherAttempt()
    {
        using var cancellation = new CancellationTokenSource();
        var reachedDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var strategy = new RecordingStrategy(() => reachedDelay.TrySetResult(), TimeSpan.FromDays(1));
        var executor = new RetryExecutor(strategy, new ExceptionTypeRetryCondition(typeof(Exception)));
        var attempts = 0;
        var execution = executor.ExecuteAsync<int>(() =>
        {
            attempts++;
            throw new InvalidOperationException("retry");
        }, cancellation.Token);

        await reachedDelay.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellableEntries_PassCallerTokenAndStopInFlightOperation(bool returnsValue)
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedToken = CancellationToken.None;
        var attempts = 0;
        var executor = CreateExecutor();

        async Task Operation(CancellationToken token)
        {
            observedToken = token;
            attempts++;
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }

        Task execution = returnsValue
            ? executor.ExecuteCancellableAsync(async token => { await Operation(token); return 42; }, cancellation.Token)
            : executor.ExecuteCancellableAsync(Operation, cancellation.Token);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(cancellation.Token, observedToken);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Exhaustion_PreservesExceptionIdentityAndOriginalThrowSite()
    {
        var failure = new InvalidOperationException("original failure");
        var attempts = 0;
        var executor = CreateExecutor();

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync<int>(() =>
        {
            attempts++;
            ThrowFromOriginalOperation(failure);
            return Task.FromResult(42);
        }));

        Assert.Same(failure, actual);
        Assert.Contains(nameof(ThrowFromOriginalOperation), actual.StackTrace);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task IndependentOperationCancellation_StillUsesExistingRetryCondition()
    {
        using var operationCancellation = new CancellationTokenSource();
        operationCancellation.Cancel();
        var attempts = 0;
        var executor = new RetryExecutor(new FixedCountRetryStrategy(1),
            new ExceptionTypeRetryCondition(typeof(OperationCanceledException)));

        var result = await executor.ExecuteAsync(() => ++attempts == 1
            ? Task.FromCanceled<int>(operationCancellation.Token)
            : Task.FromResult(42));

        Assert.Equal(42, result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task CustomExtensions_RetainAttemptNumbersAndConditionGenericArguments()
    {
        var strategy = new RecordingStrategy();
        var condition = new RecordingCondition();
        var executor = new RetryExecutor(strategy, condition);
        var failure = new InvalidOperationException("first attempt");
        var attempts = 0;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(() =>
            ++attempts == 1 ? Task.FromException<int>(failure) : Task.FromResult(-1)));

        Assert.Equal("Result does not meet retry condition", error.Message);
        Assert.Equal(3, attempts);
        Assert.Equal(new[] { 1, 2, 3 }, strategy.Attempts);
        Assert.Equal(new[] { 1, 2 }, strategy.DelayedAttempts);
        Assert.Equal(new[] { typeof(object), typeof(int), typeof(int) }, condition.GenericArguments);
        Assert.Same(failure, strategy.Failures[0]);
        Assert.All(strategy.Failures.GetRange(1, 2), exception => Assert.IsType<InvalidOperationException>(exception));
    }

    [Fact]
    public async Task WithTimeout_DoesNotCancelFirstSuccessfulOperation()
    {
        var executor = RetryBuilder.Create().WithTimeout(TimeSpan.Zero).OnException<Exception>().Build();
        var result = await executor.ExecuteAsync(async () =>
        {
            await Task.Yield();
            return 42;
        });

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task WithTimeout_ExhaustedBudgetStopsAfterFailure()
    {
        var executor = RetryBuilder.Create().WithTimeout(TimeSpan.Zero).OnException<Exception>().Build();
        var attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync<int>(() =>
        {
            attempts++;
            throw new InvalidOperationException("failed");
        }));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task SynchronousEntry_DoesNotRequirePumpingCallerSynchronizationContext()
    {
        var context = new HeldSynchronizationContext();
        var completed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstOperationThread = 0;
        var worker = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var executor = CreateExecutor(TimeSpan.FromMilliseconds(50));
                var attempts = 0;
                completed.SetResult(executor.Execute(() =>
                {
                    if (++attempts == 1)
                    {
                        firstOperationThread = Environment.CurrentManagedThreadId;
                        throw new InvalidOperationException("retry");
                    }
                    return 42;
                }));
            }
            catch (Exception exception)
            {
                completed.SetException(exception);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }
        }) { IsBackground = true };
        worker.Start();

        Task first;
        try
        {
            first = await Task.WhenAny(completed.Task, context.Posted).WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            // Release captured continuations even when testing the old deadlocking implementation.
            context.Release();
            Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
        }

        Assert.Equal(42, await completed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Same(completed.Task, first);
        Assert.Equal(worker.ManagedThreadId, firstOperationThread);
    }

    private static RetryExecutor CreateExecutor(TimeSpan delay = default) =>
        new(new FixedCountRetryStrategy(2, delay), new ExceptionTypeRetryCondition(typeof(Exception)));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromOriginalOperation(Exception failure) => throw failure;

    private sealed class RecordingStrategy(Action? onDelay = null, TimeSpan delay = default) : IRetryStrategy
    {
        public List<int> Attempts { get; } = [];
        public List<int> DelayedAttempts { get; } = [];
        public List<Exception?> Failures { get; } = [];

        public bool ShouldRetry(RetryContext context)
        {
            Attempts.Add(context.Attempt);
            Failures.Add(context.LastException);
            return context.Attempt <= 2;
        }

        public TimeSpan GetDelay(RetryContext context)
        {
            DelayedAttempts.Add(context.Attempt);
            onDelay?.Invoke();
            return delay;
        }
    }

    private sealed class RecordingCondition : IRetryCondition
    {
        public List<Type> GenericArguments { get; } = [];

        public bool ShouldRetry<T>(T result, Exception? exception)
        {
            GenericArguments.Add(typeof(T));
            return exception != null || result is int value && value < 0;
        }
    }

    private sealed class HeldSynchronizationContext : SynchronizationContext
    {
        private readonly object _gate = new();
        private readonly System.Collections.Generic.Queue<(SendOrPostCallback Callback, object? State)> _pending = new();
        private readonly TaskCompletionSource _posted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _released;

        public Task Posted => _posted.Task;

        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_gate)
            {
                if (!_released)
                {
                    _pending.Enqueue((callback, state));
                    _posted.TrySetResult();
                    return;
                }
            }
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }

        public void Release()
        {
            lock (_gate)
            {
                _released = true;
                while (_pending.TryDequeue(out var continuation))
                    ThreadPool.QueueUserWorkItem(_ => continuation.Callback(continuation.State));
            }
        }
    }
}
