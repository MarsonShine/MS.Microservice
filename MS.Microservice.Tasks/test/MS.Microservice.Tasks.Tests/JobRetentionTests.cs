using System.Data.Common;
using System.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MS.Microservice.Tasks.Tests;

public sealed class JobRetentionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task PruneSelectsEachTerminalStateAndKeepsOtherScopes()
    {
        await using var fixture = await JobDatabase.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var retention = scope.ServiceProvider.GetRequiredService<IJobRetentionStore>();
        var jobs = new Dictionary<JobState, Guid>();
        foreach (var state in new[] { JobState.Succeeded, JobState.Failed, JobState.Cancelled, JobState.Interrupted })
        {
            var job = await TerminalAsync(store, "owner", state);
            jobs.Add(state, job.Job.JobId);
            await TerminalAsync(store, "Owner", state, job.Job.JobId);
        }
        fixture.Clock.Advance(TimeSpan.FromTicks(1));

        foreach (var (state, id) in jobs)
        {
            Assert.Equal(1, await retention.PruneAsync("owner", state, fixture.Clock.GetUtcNow()));
            Assert.Null(await store.GetAsync("owner", id));
            Assert.Equal(state, (await store.GetAsync("Owner", id))!.State);
        }
        Assert.Empty(await store.ListAsync("owner"));
        Assert.Equal(4, (await store.ListAsync("Owner")).Count);
    }

    [Fact]
    public async Task PruneUsesStrictUtcCutoffAndPreservesActiveJobsWithNoFinishTime()
    {
        await using var fixture = await JobDatabase.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var retention = scope.ServiceProvider.GetRequiredService<IJobRetentionStore>();
        var older = await TerminalAsync(store, "owner", JobState.Succeeded);
        fixture.Clock.Advance(TimeSpan.FromTicks(1));
        var cutoff = fixture.Clock.GetUtcNow();
        var equal = await TerminalAsync(store, "owner", JobState.Succeeded);
        fixture.Clock.Advance(TimeSpan.FromTicks(1));
        var newer = await TerminalAsync(store, "owner", JobState.Succeeded);
        var queued = await store.SubmitAsync(Submission("owner"));
        var running = await store.SubmitAsync(Submission("owner"));
        Assert.NotNull(await store.TryStartAsync("owner", running.JobId));

        Assert.Equal(1, await retention.PruneAsync("owner", JobState.Succeeded, cutoff.ToOffset(TimeSpan.FromHours(8))));

        Assert.Null(await store.GetAsync("owner", older.Job.JobId));
        Assert.NotNull(await store.GetAsync("owner", equal.Job.JobId));
        Assert.NotNull(await store.GetAsync("owner", newer.Job.JobId));
        Assert.Null((await store.GetAsync("owner", queued.JobId))!.FinishedAtUtc);
        Assert.Null((await store.GetAsync("owner", running.JobId))!.FinishedAtUtc);
    }

    [Fact]
    public async Task PruneBoundsEachBatchAndReturnsTheActualDeletionCount()
    {
        await using var fixture = await JobDatabase.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var retention = scope.ServiceProvider.GetRequiredService<IJobRetentionStore>();
        for (var index = 0; index < 3; index++) await TerminalAsync(store, "owner", JobState.Failed);
        fixture.Clock.Advance(TimeSpan.FromTicks(1));

        Assert.Equal(2, await retention.PruneAsync("owner", JobState.Failed, fixture.Clock.GetUtcNow(), take: 2));
        Assert.Single(await store.ListAsync("owner"));
        Assert.Equal(1, await retention.PruneAsync("owner", JobState.Failed, fixture.Clock.GetUtcNow(), take: 2));
        Assert.Equal(0, await retention.PruneAsync("owner", JobState.Failed, fixture.Clock.GetUtcNow(), take: 2));
    }

    [Fact]
    public async Task PruneCascadesAllAttemptsWithoutDeletingAnotherScopesSameIdentity()
    {
        await using var fixture = await JobDatabase.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var retention = scope.ServiceProvider.GetRequiredService<IJobRetentionStore>();
        var first = await TerminalAsync(store, "owner", JobState.Failed);
        Assert.Equal(JobUpdateResult.Applied, await store.RetryAsync("owner", first.Job.JobId));
        var second = Assert.IsType<JobExecution>(await store.TryStartAsync("owner", first.Job.JobId));
        Assert.True(await store.CompleteAsync(second.Attempt));
        await TerminalAsync(store, "other", JobState.Succeeded, first.Job.JobId);
        Assert.Equal(3, await CountAttemptsAsync(fixture, first.Job.JobId));
        fixture.Clock.Advance(TimeSpan.FromTicks(1));

        Assert.Equal(1, await retention.PruneAsync("owner", JobState.Succeeded, fixture.Clock.GetUtcNow()));

        Assert.Equal(1, await CountAttemptsAsync(fixture, first.Job.JobId));
        Assert.Empty(await store.GetAttemptsAsync("owner", first.Job.JobId));
        Assert.Single(await store.GetAttemptsAsync("other", first.Job.JobId));
        Assert.NotNull(await store.GetAsync("other", first.Job.JobId));
    }

    [Fact]
    public async Task RetryAfterCandidateSelectionPreventsDeletion()
    {
        var barrier = new CommandBarrier("DELETE");
        await using var fixture = await JobDatabase.CreateAsync(interceptors: [barrier]);
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var retention = scope.ServiceProvider.GetRequiredService<IJobRetentionStore>();
        var job = await TerminalAsync(store, "owner", JobState.Failed);
        fixture.Clock.Advance(TimeSpan.FromTicks(1));
        barrier.Arm();
        var pruning = retention.PruneAsync("owner", JobState.Failed, fixture.Clock.GetUtcNow());
        try
        {
            await barrier.Entered.Task.WaitAsync(Deadline);
            Assert.Equal(JobUpdateResult.Applied, await store.RetryAsync("owner", job.Job.JobId));
        }
        finally { barrier.Release.TrySetResult(); }

        Assert.Equal(0, await pruning.WaitAsync(Deadline));
        Assert.Equal(JobState.Queued, (await store.GetAsync("owner", job.Job.JobId))!.State);
        Assert.Single(await store.GetAttemptsAsync("owner", job.Job.JobId));
    }

    [Fact]
    public async Task DeletionBeforeRetryReturnsNotFoundAndResubmissionRejectsTheOldToken()
    {
        var barrier = new CommandBarrier("UPDATE");
        await using var fixture = await JobDatabase.CreateAsync(interceptors: [barrier]);
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var retention = scope.ServiceProvider.GetRequiredService<IJobRetentionStore>();
        var old = await TerminalAsync(store, "owner", JobState.Failed);
        fixture.Clock.Advance(TimeSpan.FromTicks(1));
        barrier.Arm();
        var retrying = store.RetryAsync("owner", old.Job.JobId);
        try
        {
            await barrier.Entered.Task.WaitAsync(Deadline);
            Assert.Equal(1, await retention.PruneAsync("owner", JobState.Failed, fixture.Clock.GetUtcNow()));
        }
        finally { barrier.Release.TrySetResult(); }
        Assert.Equal(JobUpdateResult.NotFound, await retrying.WaitAsync(Deadline));

        await store.SubmitAsync(Submission("owner", old.Job.JobId, "new-payload"));
        var current = Assert.IsType<JobExecution>(await store.TryStartAsync("owner", old.Job.JobId));
        Assert.NotEqual(old.Attempt.Token, current.Attempt.Token);
        Assert.False(await store.CompleteAsync(old.Attempt, "old-result"));
        Assert.False(await store.ReportProgressAsync(old.Attempt, 99));
        Assert.True(await store.CompleteAsync(current.Attempt, "new-result"));
        var result = (await store.GetAsync("owner", old.Job.JobId))!;
        Assert.Equal("new-result", result.ResultReference);
        Assert.Contains("new-payload", result.PayloadJson);
        Assert.Equal(1, result.AttemptCount);
        Assert.Single(await store.GetAttemptsAsync("owner", old.Job.JobId));
    }

    [Fact]
    public async Task CancellationStopsTheBatchAfterItsAlreadyCommittedDeletion()
    {
        using var cancellation = new CancellationTokenSource();
        var interceptor = new CancelAfterDeletion(cancellation);
        await using var fixture = await JobDatabase.CreateAsync(interceptors: [interceptor]);
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var retention = scope.ServiceProvider.GetRequiredService<IJobRetentionStore>();
        for (var index = 0; index < 3; index++) await TerminalAsync(store, "owner", JobState.Succeeded);
        fixture.Clock.Advance(TimeSpan.FromTicks(1));
        using var alreadyCanceled = new CancellationTokenSource();
        alreadyCanceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retention.PruneAsync("owner", JobState.Succeeded,
            fixture.Clock.GetUtcNow(), cancellationToken: alreadyCanceled.Token));
        Assert.Equal(3, (await store.ListAsync("owner")).Count);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retention.PruneAsync("owner", JobState.Succeeded,
            fixture.Clock.GetUtcNow(), cancellationToken: cancellation.Token));

        Assert.Equal(1, interceptor.Deleted);
        Assert.Equal(2, (await store.ListAsync("owner")).Count);
    }

    [Fact]
    public async Task PruneUsesAnIndependentScopeWithoutSavingDirtyCallerEntitiesAndRejectsTransactions()
    {
        await using var fixture = await JobDatabase.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var retention = scope.ServiceProvider.GetRequiredService<IJobRetentionStore>();
        var context = scope.ServiceProvider.GetRequiredService<JobTestContext>();
        await TerminalAsync(store, "owner", JobState.Succeeded);
        fixture.Clock.Advance(TimeSpan.FromTicks(1));
        var business = new BusinessRecord { Id = Guid.NewGuid(), Value = "must-not-be-saved" };
        context.Add(business);

        Assert.Equal(1, await retention.PruneAsync("owner", JobState.Succeeded, fixture.Clock.GetUtcNow()));

        Assert.Equal(EntityState.Added, context.Entry(business).State);
        await using (var check = fixture.Services.CreateAsyncScope())
            Assert.Empty(await check.ServiceProvider.GetRequiredService<JobTestContext>().Business.ToListAsync());
        await using (var transaction = await context.Database.BeginTransactionAsync())
            await Assert.ThrowsAsync<InvalidOperationException>(() => retention.PruneAsync("owner", JobState.Succeeded, fixture.Clock.GetUtcNow()));
        using (var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            await Assert.ThrowsAsync<InvalidOperationException>(() => retention.PruneAsync("owner", JobState.Succeeded, fixture.Clock.GetUtcNow()));
    }

    [Fact]
    public async Task PruneRejectsNonterminalStatesFutureCutoffsAndInvalidBatchBounds()
    {
        await using var fixture = await JobDatabase.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var retention = scope.ServiceProvider.GetRequiredService<IJobRetentionStore>();
        foreach (var state in new[] { JobState.Queued, JobState.Running, (JobState)999 })
            await Assert.ThrowsAnyAsync<ArgumentException>(() => retention.PruneAsync("owner", state, fixture.Clock.GetUtcNow()));
        foreach (var take in new[] { 0, 1001 })
            await Assert.ThrowsAnyAsync<ArgumentException>(() => retention.PruneAsync("owner", JobState.Failed, fixture.Clock.GetUtcNow(), take));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => retention.PruneAsync("owner", JobState.Failed, fixture.Clock.GetUtcNow().AddTicks(1)));
    }

    private static JobSubmission Submission(string scope, Guid? id = null, string value = "payload")
        => JobSubmission.Create(id ?? Guid.NewGuid(), scope, "retention.test", 1, new { value });

    private static async Task<JobExecution> TerminalAsync(IJobStore store, string scope, JobState state, Guid? id = null)
    {
        var job = await store.SubmitAsync(Submission(scope, id));
        var execution = Assert.IsType<JobExecution>(await store.TryStartAsync(scope, job.JobId));
        switch (state)
        {
            case JobState.Succeeded: Assert.True(await store.CompleteAsync(execution.Attempt)); break;
            case JobState.Failed: Assert.True(await store.FailAsync(execution.Attempt, "test_failure")); break;
            case JobState.Interrupted: Assert.True(await store.InterruptAsync(execution.Attempt)); break;
            case JobState.Cancelled:
                Assert.Equal(JobUpdateResult.Applied, await store.CancelAsync(scope, job.JobId));
                Assert.True(await store.CompleteAsync(execution.Attempt));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(state));
        }
        return execution;
    }

    private static async Task<int> CountAttemptsAsync(JobDatabase fixture, Guid id)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"JobAttempts\" WHERE \"JobId\" = @id";
        command.Parameters.AddWithValue("@id", id);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed class CommandBarrier(string verb) : DbCommandInterceptor
    {
        private int _armed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Volatile.Write(ref _armed, 1);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (IsJobCommand(command, verb) && Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class CancelAfterDeletion(CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        public int Deleted { get; private set; }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (IsJobCommand(command, "DELETE") && result > 0)
            {
                Deleted += result;
                cancellation.Cancel();
            }
            return ValueTask.FromResult(result);
        }
    }

    private static bool IsJobCommand(DbCommand command, string verb)
        => command.CommandText.TrimStart().StartsWith(verb + " ", StringComparison.OrdinalIgnoreCase)
            && command.CommandText.Contains("\"Jobs\"", StringComparison.Ordinal);
}
