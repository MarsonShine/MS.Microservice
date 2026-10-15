using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MS.Microservice.Tasks.Tests;

public sealed class JobExecutorTests
{
    [Fact]
    public async Task Cancellation_KeepsCapacityUntilTheHandlerAndItsScopeReallyEnd()
    {
        var probe = new HandlerProbe();
        await using var fixture = await CreateAsync(probe);
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var executor = fixture.Services.GetRequiredService<JobExecutor>();
        var first = await SubmitAsync(store, new("first", Wait: true, IgnoreCancellation: true, HoldCleanup: true));
        await executor.StartAsync();
        await executor.TickAsync();
        var signal = probe.For("first");
        await signal.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await store.CancelAsync("owner", first.JobId);
        await signal.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await SubmitAsync(store, new("second"));
        await executor.TickAsync();
        Assert.Equal(JobState.Running, (await store.GetAsync("owner", first.JobId))!.State);
        Assert.Equal(JobState.Queued, (await store.GetAsync("owner", second.JobId))!.State);

        signal.Release.TrySetResult();
        await signal.CleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await executor.TickAsync();
        Assert.Equal(JobState.Running, (await store.GetAsync("owner", first.JobId))!.State);
        Assert.Equal(JobState.Queued, (await store.GetAsync("owner", second.JobId))!.State);
        signal.CleanupRelease.TrySetResult();
        await WaitForStateAsync(store, first.JobId, JobState.Cancelled);
        await executor.TickAsync();
        await WaitForStateAsync(store, second.JobId, JobState.Succeeded, executor);
        await executor.StopAsync();
        Assert.Equal(2, probe.InstanceIds.Distinct().Count());
        Assert.Equal(2, probe.Disposed);
    }

    [Fact]
    public async Task Timeout_FailureAndUnknownContracts_DoNotAutomaticallyRetry_AndQueuesRunIndependently()
    {
        var probe = new HandlerProbe();
        await using var fixture = await CreateAsync(probe, timeout: TimeSpan.FromMilliseconds(150));
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var executor = fixture.Services.GetRequiredService<JobExecutor>();
        var slow = await SubmitAsync(store, new("slow", Wait: true));
        var independent = await SubmitAsync(store, new("fast"), handler: "fast", queue: "fast");
        await executor.StartAsync();
        await executor.TickAsync();
        await WaitForStateAsync(store, independent.JobId, JobState.Succeeded);
        await WaitForStateAsync(store, slow.JobId, JobState.Failed);
        Assert.Equal("execution_timeout", (await store.GetAsync("owner", slow.JobId))!.ErrorCode);

        var failed = await SubmitAsync(store, new("failure", Fail: true), handler: "fast", queue: "fast");
        await executor.TickAsync();
        await WaitForStateAsync(store, failed.JobId, JobState.Failed, executor);
        Assert.Equal("handler_failed", (await store.GetAsync("owner", failed.JobId))!.ErrorCode);
        var unknown = await SubmitAsync(store, new("unknown"), version: 2);
        await executor.TickAsync();
        await WaitForStateAsync(store, unknown.JobId, JobState.Failed, executor);
        Assert.Equal("handler_not_registered", (await store.GetAsync("owner", unknown.JobId))!.ErrorCode);
        var invalidResult = await SubmitAsync(store, new(new string('x', 4096)), handler: "fast", queue: "fast");
        await WaitForStateAsync(store, invalidResult.JobId, JobState.Failed, executor);
        Assert.Equal("result_reference_invalid", (await store.GetAsync("owner", invalidResult.JobId))!.ErrorCode);
        await executor.TickAsync();
        Assert.Equal(1, (await store.GetAsync("owner", slow.JobId))!.AttemptCount);
        Assert.Equal(1, (await store.GetAsync("owner", failed.JobId))!.AttemptCount);
        Assert.False(probe.Signals.ContainsKey("unknown"));
        await executor.StopAsync();
    }

    [Fact]
    public async Task Shutdown_IsInterruptedAndWaitsForCleanup_WhileUserCancellationStaysCancelled()
    {
        var probe = new HandlerProbe();
        await using var fixture = await CreateAsync(probe);
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var executor = fixture.Services.GetRequiredService<JobExecutor>();
        var running = await SubmitAsync(store, new("shutdown", Wait: true, HoldCleanup: true));
        await executor.StartAsync();
        await executor.TickAsync();
        var signal = probe.For("shutdown");
        await signal.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = executor.StopAsync();
        await signal.CleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stop.IsCompleted);
        Assert.Equal(JobState.Running, (await store.GetAsync("owner", running.JobId))!.State);
        signal.CleanupRelease.TrySetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(JobState.Interrupted, (await store.GetAsync("owner", running.JobId))!.State);
        Assert.False((await store.GetAsync("owner", running.JobId))!.CancellationRequested);
        Assert.Equal(1, probe.Disposed);

        await using var safeFixture = await CreateAsync(new HandlerProbe(), safe: true);
        await using var safeScope = safeFixture.Services.CreateAsyncScope();
        var safeStore = safeScope.ServiceProvider.GetRequiredService<IJobStore>();
        var safeJob = await SubmitAsync(safeStore, new("safe-stop", Wait: true));
        var safeExecutor = safeFixture.Services.GetRequiredService<JobExecutor>();
        await safeExecutor.StartAsync();
        await safeExecutor.TickAsync();
        await safeFixture.Services.GetRequiredService<HandlerProbe>().For("safe-stop").Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await safeExecutor.StopAsync();
        Assert.Equal(JobState.Queued, (await safeStore.GetAsync("owner", safeJob.JobId))!.State);
        Assert.Equal(JobState.Interrupted, Assert.Single(await safeStore.GetAttemptsAsync("owner", safeJob.JobId)).State);

        var blockedProbe = new HandlerProbe();
        await using var blockedFixture = await CreateAsync(blockedProbe);
        await using var blockedScope = blockedFixture.Services.CreateAsyncScope();
        var blockedStore = blockedScope.ServiceProvider.GetRequiredService<IJobStore>();
        var blockedJob = await SubmitAsync(blockedStore,
            new("blocked-callback", Wait: true, IgnoreCancellation: true, BlockCancellationCallback: true));
        var blockedExecutor = blockedFixture.Services.GetRequiredService<JobExecutor>();
        await blockedExecutor.StartAsync();
        await blockedExecutor.TickAsync();
        var blocked = blockedProbe.For("blocked-callback");
        await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            using var stopDeadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var stopWithDeadline = blockedExecutor.StopAsync(stopDeadline.Token);
            await blocked.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopWithDeadline.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(JobState.Running, (await blockedStore.GetAsync("owner", blockedJob.JobId))!.State);
        }
        finally
        {
            blocked.CancellationRelease.TrySetResult();
            blocked.Release.TrySetResult();
            await blockedExecutor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(JobState.Interrupted, (await blockedStore.GetAsync("owner", blockedJob.JobId))!.State);
    }

    [Fact]
    public async Task StartupRecovery_RequeuesOnlySafeUncancelledJobsWithinTheirAttemptBudget()
    {
        var probe = new HandlerProbe();
        await using var fixture = await CreateAsync(probe, safe: true);
        var ids = new List<Guid>();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
            foreach (var item in new[]
            {
                ("safe", "controlled", 3, false), ("manual", "fast", 3, false),
                ("cancelled", "controlled", 3, true), ("exhausted", "controlled", 1, false)
            })
            {
                var job = await SubmitAsync(store, new(item.Item1), item.Item2,
                    queue: item.Item2 == "fast" ? "fast" : "default", maximumAttempts: item.Item3);
                ids.Add(job.JobId);
                Assert.NotNull(await store.TryStartAsync("owner", job.JobId));
                if (item.Item4) await store.CancelAsync("owner", job.JobId);
            }
        }
        await fixture.RestartAsync();
        var executor = fixture.Services.GetRequiredService<JobExecutor>();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
            await executor.TickAsync();
            Assert.Equal(4, (await store.GetRunningExecutionsAsync()).Count);
            await executor.StartAsync();
            Assert.Empty(await store.GetRunningExecutionsAsync());
            Assert.Equal(JobState.Queued, (await store.GetAsync("owner", ids[0]))!.State);
            Assert.Equal(JobState.Interrupted, (await store.GetAsync("owner", ids[1]))!.State);
            Assert.Equal(JobState.Cancelled, (await store.GetAsync("owner", ids[2]))!.State);
            Assert.Equal(JobState.Interrupted, (await store.GetAsync("owner", ids[3]))!.State);
            Assert.Equal(JobState.Interrupted, Assert.Single(await store.GetAttemptsAsync("owner", ids[0])).State);
            await executor.TickAsync();
            await WaitForStateAsync(store, ids[0], JobState.Succeeded);
            Assert.Equal(2, (await store.GetAsync("owner", ids[0]))!.AttemptCount);
            Assert.Equal(2, (await store.GetAttemptsAsync("owner", ids[0])).Count);
        }
        await executor.StopAsync();
    }

    private static Task<JobDatabase> CreateAsync(HandlerProbe probe, TimeSpan? timeout = null, bool safe = false)
        => JobDatabase.CreateAsync(services =>
        {
            services.AddSingleton(probe);
            services.AddTaskExecution(options =>
            {
                options.CancellationPollInterval = TimeSpan.FromMilliseconds(25);
                options.AddQueue("fast", 1);
            });
            services.AddJobHandler<ControlledPayload, ControlledHandler>("controlled", configure: options =>
            {
                options.Timeout = timeout ?? TimeSpan.FromSeconds(10);
                options.SafeToRetry = safe;
            });
            services.AddJobHandler<ControlledPayload, ControlledHandler>("fast", configure: options => options.Queue = "fast");
        });

    private static Task<JobSnapshot> SubmitAsync(IJobStore store, ControlledPayload payload, string handler = "controlled",
        string queue = "default", int version = 1, int maximumAttempts = 3)
        => store.SubmitAsync(JobSubmission.Create(Guid.NewGuid(), "owner", handler, version, payload,
            queue: queue, maximumAttempts: maximumAttempts));

    private static async Task WaitForStateAsync(IJobStore store, Guid jobId, JobState state, JobExecutor? executor = null)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (executor is not null) await executor.TickAsync();
            if ((await store.GetAsync("owner", jobId))?.State == state) return;
            await Task.Delay(10);
        }
        Assert.Equal(state, (await store.GetAsync("owner", jobId))?.State);
    }
}

internal sealed record ControlledPayload(string Name, bool Wait = false, bool IgnoreCancellation = false,
    bool HoldCleanup = false, bool Fail = false, bool BlockCancellationCallback = false);

internal sealed class HandlerProbe
{
    public ConcurrentDictionary<string, HandlerSignal> Signals { get; } = new();
    public ConcurrentBag<Guid> InstanceIds { get; } = [];
    public int Disposed;
    public HandlerSignal For(string name) => Signals.GetOrAdd(name, _ => new());
}

internal sealed class HandlerSignal
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource CancellationRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource CleanupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource CleanupRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class ControlledHandler(HandlerProbe probe) : IJobHandler<ControlledPayload>, IAsyncDisposable
{
    private readonly Guid _instanceId = Guid.NewGuid();
    private HandlerSignal? _signal;
    private bool _holdCleanup;

    public async Task<string?> ExecuteAsync(ControlledPayload payload, JobExecutionContext context, CancellationToken cancellationToken)
    {
        probe.InstanceIds.Add(_instanceId);
        _signal = probe.For(payload.Name);
        _holdCleanup = payload.HoldCleanup;
        await context.ReportProgressAsync(25, "working");
        using var cancellation = cancellationToken.Register(() =>
        {
            _signal.CancellationObserved.TrySetResult();
            if (payload.BlockCancellationCallback) _signal.CancellationRelease.Task.GetAwaiter().GetResult();
        });
        _signal.Entered.TrySetResult();
        if (payload.Wait)
        {
            if (payload.IgnoreCancellation) await _signal.Release.Task;
            else await _signal.Release.Task.WaitAsync(cancellationToken);
        }
        if (payload.Fail) throw new InvalidOperationException("Controlled handler failure.");
        await context.ReportProgressAsync(75, "publishing");
        return "private:" + payload.Name;
    }

    public async ValueTask DisposeAsync()
    {
        _signal?.CleanupEntered.TrySetResult();
        if (_holdCleanup) await _signal!.CleanupRelease.Task;
        Interlocked.Increment(ref probe.Disposed);
    }
}
