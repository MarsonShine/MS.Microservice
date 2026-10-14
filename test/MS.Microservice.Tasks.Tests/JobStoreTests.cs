using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Tasks.EFCore;
using Xunit;

namespace MS.Microservice.Tasks.Tests;

public sealed class JobStoreTests
{
    [Fact]
    public async Task ScopeIdentity_RejectsUnpairedSurrogates_InSubmissionAndLookup()
    {
        await using var fixture = await JobDatabase.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        foreach (var invalid in new[] { "owner-\uD800", "owner-\uDC00" })
        {
            Assert.Throws<EncoderFallbackException>(() => JobSubmission.Create(Guid.NewGuid(), invalid, "test", 1, new { }));
            await Assert.ThrowsAsync<EncoderFallbackException>(() => store.GetAsync(invalid, Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task Submission_FreezesPayload_PreservesIdentity_AndNeverSavesCallerChanges()
    {
        await using var fixture = await JobDatabase.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<JobTestContext>();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var business = new BusinessRecord { Id = Guid.NewGuid(), Value = "not committed" };
        context.Add(business);
        var payload = new Dictionary<string, string> { ["file"] = "original.png" };
        var submission = JobSubmission.Create(Guid.NewGuid(), "Owner", "image.resize", 1, payload);
        payload["file"] = "changed.png";

        var created = await store.SubmitAsync(submission);
        Assert.Equal("original.png", JsonDocument.Parse(created.PayloadJson).RootElement.GetProperty("file").GetString());
        Assert.Equal(EntityState.Added, context.Entry(business).State);
        await using (var readScope = fixture.Services.CreateAsyncScope())
            Assert.Empty(await readScope.ServiceProvider.GetRequiredService<JobTestContext>().Business.ToListAsync());

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(created, await store.SubmitAsync(submission));
        await Assert.ThrowsAsync<JobSubmissionConflictException>(() => store.SubmitAsync(
            JobSubmission.Create(submission.JobId, submission.Scope, "image.resize", 1, new { file = "other.png" })));
        Assert.Null(await store.GetAsync("owner", created.JobId));
        Assert.Empty(await store.ListAsync("other-owner"));
        Assert.Equal(JobUpdateResult.NotFound, await store.CancelAsync("other-owner", created.JobId));
        Assert.Equal(JobUpdateResult.NotFound, await store.RetryAsync("other-owner", created.JobId));
        var other = await store.SubmitAsync(JobSubmission.Create(created.JobId, "owner", "image.resize", 1, new { file = "other.png" }));
        Assert.Equal("owner", other.Scope);
        Assert.Single(await store.ListAsync("Owner"));
    }

    [Fact]
    public async Task Staging_ParticipatesInBusinessCommitAndRollback_WithoutSavingOrOwningTheTransaction()
    {
        await using var fixture = await JobDatabase.CreateAsync();
        var rolledBack = JobSubmission.Create(Guid.NewGuid(), "owner", "report.export", 1, new { report = 1 });
        var committed = JobSubmission.Create(Guid.NewGuid(), "owner", "report.export", 1, new { report = 2 });
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<JobTestContext>();
            var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.StageAsync(rolledBack));
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.Add(new BusinessRecord { Id = Guid.NewGuid(), Value = "rollback" });
            var staged = await store.StageAsync(rolledBack);
            Assert.Equal(staged, await store.StageAsync(rolledBack));
            Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Added, entry.State));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SubmitAsync(rolledBack));
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<JobTestContext>();
            var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
            Assert.Null(await store.GetAsync("owner", rolledBack.JobId));
            Assert.Empty(await context.Business.ToListAsync());
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.Add(new BusinessRecord { Id = Guid.NewGuid(), Value = "commit" });
            await store.StageAsync(committed);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IJobStore>().GetAsync("owner", committed.JobId));
            Assert.Equal("commit", (await scope.ServiceProvider.GetRequiredService<JobTestContext>().Business.SingleAsync()).Value);
        }
    }

    [Fact]
    public async Task Executions_RejectStaleTokens_KeepMonotonicProgress_AndRetainBoundedAttemptHistory()
    {
        await using var fixture = await JobDatabase.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var submission = JobSubmission.Create(Guid.NewGuid(), "owner", "report.export", 1, new { report = 1 }, maximumAttempts: 2);
        await store.SubmitAsync(submission);
        var first = Assert.IsType<JobExecution>(await store.TryStartAsync("owner", submission.JobId));
        Assert.Null(await store.TryStartAsync("owner", submission.JobId));
        Assert.Equal(JobUpdateResult.Rejected, await store.RetryAsync("owner", submission.JobId));
        Assert.True(await store.ReportProgressAsync(first.Attempt, 40, "rendering"));
        Assert.False(await store.ReportProgressAsync(first.Attempt, 20));
        Assert.False(await store.ReportProgressAsync(first.Attempt with { Token = Guid.NewGuid() }, 50));
        Assert.False(await store.CompleteAsync(first.Attempt with { Scope = "other-owner" }));
        Assert.True(await store.FailAsync(first.Attempt, "renderer_failed"));
        Assert.Equal(JobUpdateResult.Applied, await store.RetryAsync("owner", submission.JobId));

        var second = Assert.IsType<JobExecution>(await store.TryStartAsync("owner", submission.JobId));
        Assert.Equal(2, second.Job.AttemptCount);
        Assert.Equal(0, second.Job.Progress);
        Assert.NotEqual(first.Attempt.Token, second.Attempt.Token);
        Assert.False(await store.ReportProgressAsync(first.Attempt, 99));
        Assert.False(await store.CompleteAsync(first.Attempt, "old-result"));
        Assert.True(await store.ReportProgressAsync(second.Attempt, 70));
        Assert.True(await store.FailAsync(second.Attempt, "renderer_failed_again"));
        Assert.Equal(JobUpdateResult.Rejected, await store.RetryAsync("owner", submission.JobId));
        Assert.Null(await store.TryStartAsync("owner", submission.JobId));

        var history = await store.GetAttemptsAsync("owner", submission.JobId);
        Assert.Equal(2, history.Count);
        Assert.Equal(40, history[0].Progress);
        Assert.Equal("renderer_failed", history[0].ErrorCode);
        Assert.Equal(70, history[1].Progress);
        Assert.All(history, attempt => Assert.Equal(JobState.Failed, attempt.State));
        Assert.All(history, attempt => Assert.NotNull(attempt.FinishedAtUtc));
        Assert.Empty(await store.GetAttemptsAsync("other-owner", submission.JobId));
    }

    [Fact]
    public async Task Cancellation_IsCooperativeWhileRunning_AndCommittedCompletionCannotBeOverwritten()
    {
        await using var fixture = await JobDatabase.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var queued = await store.SubmitAsync(JobSubmission.Create(Guid.NewGuid(), "owner", "image.resize", 1, new { }));
        Assert.Equal(JobUpdateResult.Applied, await store.CancelAsync("owner", queued.JobId));
        var queuedCancellation = Assert.IsType<JobSnapshot>(await store.GetAsync("owner", queued.JobId));
        Assert.Equal(JobState.Cancelled, queuedCancellation.State);
        Assert.Equal(fixture.Clock.GetUtcNow(), queuedCancellation.FinishedAtUtc);
        Assert.Null(await store.TryStartAsync("owner", queued.JobId));
        Assert.Empty(await store.GetAttemptsAsync("owner", queued.JobId));
        Assert.Equal(JobUpdateResult.Applied, await store.CancelAsync("owner", queued.JobId));
        Assert.Equal(JobUpdateResult.Applied, await store.RetryAsync("owner", queued.JobId));

        var cancelled = Assert.IsType<JobExecution>(await store.TryStartAsync("owner", queued.JobId));
        await store.ReportProgressAsync(cancelled.Attempt, 30);
        Assert.Equal(JobUpdateResult.Applied, await store.CancelAsync("owner", queued.JobId));
        var stopping = Assert.IsType<JobSnapshot>(await store.GetAsync("owner", queued.JobId));
        Assert.Equal(JobState.Running, stopping.State);
        Assert.True(stopping.CancellationRequested);
        Assert.Null(stopping.FinishedAtUtc);
        Assert.Equal(JobUpdateResult.Rejected, await store.RetryAsync("owner", queued.JobId));
        Assert.False(await store.ReportProgressAsync(cancelled.Attempt, 60));
        Assert.True(await store.CompleteAsync(cancelled.Attempt, "must-not-publish"));
        var terminal = Assert.IsType<JobSnapshot>(await store.GetAsync("owner", queued.JobId));
        Assert.Equal(JobState.Cancelled, terminal.State);
        Assert.Equal(30, terminal.Progress);
        Assert.Null(terminal.ResultReference);
        Assert.Equal(JobState.Cancelled, Assert.Single(await store.GetAttemptsAsync("owner", queued.JobId)).State);

        Assert.Equal(JobUpdateResult.Applied, await store.RetryAsync("owner", queued.JobId));
        var success = Assert.IsType<JobExecution>(await store.TryStartAsync("owner", queued.JobId));
        Assert.True(await store.CompleteAsync(success.Attempt, "private:result.png"));
        Assert.Equal(JobUpdateResult.Rejected, await store.CancelAsync("owner", queued.JobId));
        var result = Assert.IsType<JobSnapshot>(await store.GetAsync("owner", queued.JobId));
        Assert.Equal(JobState.Succeeded, result.State);
        Assert.Equal(100, result.Progress);
        Assert.False(result.CancellationRequested);
        Assert.Equal("private:result.png", result.ResultReference);
        Assert.False(await store.InterruptAsync(success.Attempt));
    }

    [Fact]
    public async Task DueTimesAndInterruptedHistory_SurviveAServiceProviderRestart_WithUtcTickPrecision()
    {
        await using var fixture = await JobDatabase.CreateAsync();
        var due = fixture.Clock.GetUtcNow().AddMinutes(5).ToOffset(TimeSpan.FromHours(8));
        var submission = JobSubmission.Create(Guid.NewGuid(), "owner", "index.document", 1, new { version = 7 },
            queue: "index", notBeforeUtc: due);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
            var created = await store.SubmitAsync(submission);
            Assert.Equal(due.ToUniversalTime(), created.NextRunAtUtc);
            Assert.Equal(TimeSpan.Zero, created.NextRunAtUtc.Offset);
            Assert.Empty(await store.GetDueAsync("index"));
            Assert.Null(await store.TryStartAsync("owner", submission.JobId));
            fixture.Clock.Advance(TimeSpan.FromMinutes(5));
            Assert.Empty(await store.GetDueAsync());
            Assert.Single(await store.GetDueAsync("index"));
            var execution = Assert.IsType<JobExecution>(await store.TryStartAsync("owner", submission.JobId));
            Assert.True(await store.InterruptAsync(execution.Attempt));
        }
        await fixture.RestartAsync();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
            var restored = Assert.IsType<JobSnapshot>(await store.GetAsync("owner", submission.JobId));
            Assert.Equal(JobState.Interrupted, restored.State);
            Assert.Equal(due.UtcTicks, restored.NextRunAtUtc.UtcTicks);
            Assert.Single(await store.GetAttemptsAsync("owner", submission.JobId));
            Assert.Empty(await store.GetDueAsync("index"));
            Assert.Equal(JobUpdateResult.Applied, await store.RetryAsync("owner", submission.JobId));
            Assert.Single(await store.GetDueAsync("index"));
            Assert.Equal(JobState.Queued, (await store.SubmitAsync(submission)).State);
        }
    }
}
