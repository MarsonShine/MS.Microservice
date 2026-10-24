using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MS.Microservice.Tasks.Tests;

public sealed class JobFinalizationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private const string FaultMarker = "injected_final_write_failure";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalStateStorageFailureRecoversInTheSameHost(bool handlerFails)
    {
        var probe = new HandlerProbe();
        var failures = new FinalWriteFailures();
        await using var fixture = await CreateAsync(probe, failures);
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var executor = fixture.Services.GetRequiredService<JobExecutor>();
        var first = await SubmitAsync(store, new("first", HoldCleanup: true, Fail: handlerFails));
        var signal = probe.For("first");
        await executor.StartAsync();
        await executor.TickAsync();
        try
        {
            await signal.CleanupEntered.Task.WaitAsync(Deadline);
            await InstallFaultAsync(fixture);
            signal.CleanupRelease.TrySetResult();
            await failures.Second.Task.WaitAsync(Deadline);
            Assert.Equal(1, probe.Disposed);
            Assert.Equal(JobState.Running, (await store.GetAsync("owner", first.JobId))!.State);
            var second = await SubmitAsync(store, new("second"));
            await executor.TickAsync();
            Assert.Equal(JobState.Queued, (await store.GetAsync("owner", second.JobId))!.State);
            Assert.Single(probe.InstanceIds);

            await RemoveFaultAsync(fixture);

            await WaitForStateAsync(store, first.JobId, handlerFails ? JobState.Failed : JobState.Succeeded);
            Assert.Single(probe.InstanceIds);
            var finished = (await store.GetAsync("owner", first.JobId))!;
            Assert.Equal(1, finished.AttemptCount);
            Assert.Equal(handlerFails ? "handler_failed" : null, finished.ErrorCode);
            Assert.Equal(handlerFails ? null : "private:first", finished.ResultReference);
            Assert.Single(await store.GetAttemptsAsync("owner", first.JobId));
            await WaitForStateAsync(store, second.JobId, JobState.Succeeded, executor);
            Assert.Equal(2, probe.InstanceIds.Count);
        }
        finally
        {
            signal.CleanupRelease.TrySetResult();
            await RemoveFaultAsync(fixture);
            await executor.StopAsync().WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task CommittedFinalStateWithALostReplyDoesNotRerunTheHandlerOrRetainCapacity()
    {
        var probe = new HandlerProbe();
        var lostReply = new LostCommitReply();
        await using var fixture = await CreateAsync(probe, lostReply);
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var executor = fixture.Services.GetRequiredService<JobExecutor>();
        var first = await SubmitAsync(store, new("first", HoldCleanup: true));
        var signal = probe.For("first");
        await executor.StartAsync();
        await executor.TickAsync();
        try
        {
            await signal.CleanupEntered.Task.WaitAsync(Deadline);
            var second = await SubmitAsync(store, new("second"));
            lostReply.Arm();
            signal.CleanupRelease.TrySetResult();
            await lostReply.Committed.Task.WaitAsync(Deadline);

            await WaitForStateAsync(store, first.JobId, JobState.Succeeded);
            await WaitForStateAsync(store, second.JobId, JobState.Succeeded, executor);

            var finished = (await store.GetAsync("owner", first.JobId))!;
            Assert.Equal("private:first", finished.ResultReference);
            Assert.Equal(1, finished.AttemptCount);
            Assert.Equal(JobState.Succeeded, Assert.Single(await store.GetAttemptsAsync("owner", first.JobId)).State);
            Assert.Equal(2, probe.InstanceIds.Count);
        }
        finally
        {
            signal.CleanupRelease.TrySetResult();
            await executor.StopAsync().WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task FinalizationWithAStaleTokenCannotOverwriteTheSuccessorAttempt()
    {
        var probe = new HandlerProbe();
        var failures = new FinalWriteFailures();
        await using var fixture = await CreateAsync(probe, failures);
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var executor = fixture.Services.GetRequiredService<JobExecutor>();
        var first = await SubmitAsync(store, new("first", HoldCleanup: true));
        var signal = probe.For("first");
        var nextSignal = probe.For("next");
        await executor.StartAsync();
        await executor.TickAsync();
        try
        {
            await signal.CleanupEntered.Task.WaitAsync(Deadline);
            var original = Assert.Single(await store.GetRunningExecutionsAsync()).Attempt;
            await InstallFaultAsync(fixture);
            signal.CleanupRelease.TrySetResult();
            await failures.Second.Task.WaitAsync(Deadline);

            var successor = await ReplaceAttemptAsync(fixture, original);
            var next = await SubmitAsync(store, new("next", Wait: true));
            await WaitForStateAsync(store, next.JobId, JobState.Running, executor);
            await nextSignal.Entered.Task.WaitAsync(Deadline);

            Assert.Equal(JobExecutionStatus.Ended, await store.GetExecutionStatusAsync(original));
            Assert.Equal(JobExecutionStatus.Running, await store.GetExecutionStatusAsync(successor));
            var current = (await store.GetAsync("owner", first.JobId))!;
            Assert.Equal(2, current.AttemptCount);
            Assert.Null(current.ResultReference);
            var history = await store.GetAttemptsAsync("owner", first.JobId);
            Assert.Equal(new[] { JobState.Interrupted, JobState.Running }, history.Select(attempt => attempt.State));

            await RemoveFaultAsync(fixture);
            Assert.True(await store.CompleteAsync(successor, "private:new-attempt"));
            nextSignal.Release.TrySetResult();
            await WaitForStateAsync(store, next.JobId, JobState.Succeeded);
            Assert.Equal("private:new-attempt", (await store.GetAsync("owner", first.JobId))!.ResultReference);
            Assert.Equal(2, probe.InstanceIds.Count);
        }
        finally
        {
            signal.CleanupRelease.TrySetResult();
            nextSignal.Release.TrySetResult();
            await RemoveFaultAsync(fixture);
            await executor.StopAsync().WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task StopCancelsFinalizationWaitingWithoutChangingTheAlreadyChosenCompletion()
    {
        var probe = new HandlerProbe();
        var failures = new FinalWriteFailures();
        await using var fixture = await CreateAsync(probe, failures);
        Guid id;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
            var executor = fixture.Services.GetRequiredService<JobExecutor>();
            var first = await SubmitAsync(store, new("first", HoldCleanup: true));
            id = first.JobId;
            var signal = probe.For("first");
            await executor.StartAsync();
            await executor.TickAsync();
            try
            {
                await signal.CleanupEntered.Task.WaitAsync(Deadline);
                // Recover and Fail remain writable, exposing any incorrect switch away from Complete after stop.
                await InstallFaultAsync(fixture, onlySucceeded: true);
                signal.CleanupRelease.TrySetResult();
                await failures.Second.Task.WaitAsync(Deadline);

                await executor.StopAsync().WaitAsync(Deadline);

                Assert.Equal(JobState.Running, (await store.GetAsync("owner", id))!.State);
                Assert.Single(probe.InstanceIds);
            }
            finally
            {
                signal.CleanupRelease.TrySetResult();
                await RemoveFaultAsync(fixture);
                await executor.StopAsync().WaitAsync(Deadline);
            }
        }

        await fixture.RestartAsync();
        var restarted = fixture.Services.GetRequiredService<JobExecutor>();
        await restarted.StartAsync();
        await using var check = fixture.Services.CreateAsyncScope();
        var recovered = await check.ServiceProvider.GetRequiredService<IJobStore>().GetAsync("owner", id);
        Assert.Equal(JobState.Interrupted, recovered!.State);
        Assert.Equal(1, recovered.AttemptCount);
        Assert.Single(probe.InstanceIds);
        await restarted.StopAsync();
    }

    [Theory]
    [InlineData(24, 50)]
    [InlineData(51, 50)]
    [InlineData(25, 300001)]
    public void FinalizationRetryDelaysRejectOutOfRangeOrReversedBounds(int initialMilliseconds, int maximumMilliseconds)
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddTaskExecution(options =>
        {
            options.InitialFinalizationRetryDelay = TimeSpan.FromMilliseconds(initialMilliseconds);
            options.MaximumFinalizationRetryDelay = TimeSpan.FromMilliseconds(maximumMilliseconds);
        }));
    }

    [Fact]
    public void FinalizationRetryDefaultsAndBoundaryAreFinite()
    {
        var defaults = new JobExecutionOptions();
        Assert.Equal(TimeSpan.FromSeconds(1), defaults.InitialFinalizationRetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), defaults.MaximumFinalizationRetryDelay);
        new ServiceCollection().AddTaskExecution(options =>
        {
            options.InitialFinalizationRetryDelay = TimeSpan.FromMinutes(5);
            options.MaximumFinalizationRetryDelay = TimeSpan.FromMinutes(5);
        });
    }

    private static Task<JobDatabase> CreateAsync(HandlerProbe probe, params IInterceptor[] interceptors)
        => JobDatabase.CreateAsync(services =>
        {
            services.AddSingleton(probe);
            services.AddTaskExecution(options =>
            {
                options.CancellationPollInterval = TimeSpan.FromMilliseconds(25);
                options.InitialFinalizationRetryDelay = TimeSpan.FromMilliseconds(25);
                options.MaximumFinalizationRetryDelay = TimeSpan.FromMilliseconds(50);
            });
            services.AddJobHandler<ControlledPayload, ControlledHandler>("controlled");
        }, interceptors);

    private static Task<JobSnapshot> SubmitAsync(IJobStore store, ControlledPayload payload)
        => store.SubmitAsync(JobSubmission.Create(Guid.NewGuid(), "owner", "controlled", 1, payload));

    private static async Task InstallFaultAsync(JobDatabase fixture, bool onlySucceeded = false)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        // SQLite trigger DDL cannot bind parameters; only fixed test text and enum constants enter this SQL.
        var triggerSql = $"""
            CREATE TRIGGER fail_final_state BEFORE UPDATE OF "State" ON "Jobs"
            WHEN OLD."State" = {(int)JobState.Running}
                AND {(onlySucceeded ? $"NEW.\"State\" = {(int)JobState.Succeeded}" : $"NEW.\"State\" <> {(int)JobState.Running}")}
            BEGIN SELECT RAISE(ABORT, '{FaultMarker}'); END;
            """;
        await scope.ServiceProvider.GetRequiredService<JobTestContext>().Database.ExecuteSqlRawAsync(triggerSql);
    }

    private static async Task<JobAttemptToken> ReplaceAttemptAsync(JobDatabase fixture, JobAttemptToken original)
    {
        var successor = original with { Token = Guid.NewGuid() };
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Arrange the successor atomically so the old finalizer cannot observe a transient Queued state.
        command.CommandText = """
            UPDATE "JobAttempts" SET "State" = @interrupted, "FinishedAtUtcTicks" = @now, "ErrorCode" = 'interrupted'
                WHERE "JobId" = @job AND "Token" = @old AND "State" = @running;
            UPDATE "Jobs" SET "AttemptToken" = @new, "AttemptCount" = "AttemptCount" + 1,
                "Progress" = 0, "ProgressMessage" = NULL, "UpdatedAtUtcTicks" = @now, "Version" = "Version" + 1
                WHERE "Id" = @job AND "AttemptToken" = @old AND "State" = @running;
            INSERT INTO "JobAttempts" ("ScopeHash", "JobId", "Number", "Token", "State", "Progress", "StartedAtUtcTicks")
                SELECT "ScopeHash", "Id", "AttemptCount", "AttemptToken", "State", 0, @now
                FROM "Jobs" WHERE "Id" = @job AND "AttemptToken" = @new;
            """;
        command.Parameters.AddWithValue("@job", original.JobId);
        command.Parameters.AddWithValue("@old", original.Token);
        command.Parameters.AddWithValue("@new", successor.Token);
        command.Parameters.AddWithValue("@running", (int)JobState.Running);
        command.Parameters.AddWithValue("@interrupted", (int)JobState.Interrupted);
        command.Parameters.AddWithValue("@now", fixture.Clock.GetUtcNow().UtcTicks);
        Assert.Equal(3, await command.ExecuteNonQueryAsync());
        await transaction.CommitAsync();
        return successor;
    }

    private static async Task RemoveFaultAsync(JobDatabase fixture)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<JobTestContext>().Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS fail_final_state");
    }

    private static async Task WaitForStateAsync(IJobStore store, Guid id, JobState expected, JobExecutor? executor = null)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < Deadline)
        {
            if (executor is not null) await executor.TickAsync();
            if ((await store.GetAsync("owner", id))?.State == expected) return;
            await Task.Delay(10);
        }
        Assert.Equal(expected, (await store.GetAsync("owner", id))?.State);
    }

    private sealed class FinalWriteFailures : DbCommandInterceptor
    {
        private int _count;
        public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Second { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Exception is SqliteException error && error.Message.Contains(FaultMarker, StringComparison.Ordinal))
            {
                var count = Interlocked.Increment(ref _count);
                First.TrySetResult();
                if (count >= 2) Second.TrySetResult();
            }
            return Task.CompletedTask;
        }
    }

    private sealed class LostCommitReply : DbTransactionInterceptor
    {
        private int _armed;
        public TaskCompletionSource Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Volatile.Write(ref _armed, 1);
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Committed.TrySetResult();
                throw new IOException("The final commit reply was lost after the database committed.");
            }
            return Task.CompletedTask;
        }
    }
}
