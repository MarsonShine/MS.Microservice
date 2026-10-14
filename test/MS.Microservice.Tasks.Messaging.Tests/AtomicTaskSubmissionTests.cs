using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Messaging;
using Xunit;

namespace MS.Microservice.Tasks.Messaging.Tests;

public sealed class AtomicTaskSubmissionTests
{
    private const string Scope = "tenant-a";

    [Fact]
    public async Task ExistingMessagingUnitOfWork_CommitsBusinessTaskAndOutboxTogether()
    {
        await using var database = await TaskMessagingDatabase.CreateAsync();
        var exportId = Guid.NewGuid();
        var submission = Submission(Guid.NewGuid(), exportId);
        var message = Requested(submission.JobId, exportId);
        await using (var scope = database.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var context = services.GetRequiredService<WorkflowContext>();
            var store = services.GetRequiredService<IJobStore>();
            var publisher = services.GetRequiredService<IIntegrationEventPublisher>();
            await services.GetRequiredService<IUnitOfWork>().ExecuteAsync(async token =>
            {
                context.Exports.Add(new() { Id = exportId, Revision = 1 });
                var staged = await store.StageAsync(submission, token);
                Assert.Equal(JobState.Queued, staged.State);
                Assert.Equal(staged, await store.StageAsync(submission, token));
                await publisher.EnqueueAsync(message, token);
                await publisher.EnqueueAsync(message, token);
            });
        }

        await using var verification = database.Services.CreateAsyncScope();
        Assert.Equal(1, (await verification.ServiceProvider.GetRequiredService<WorkflowContext>().Exports.SingleAsync()).Revision);
        var saved = Assert.Single(await verification.ServiceProvider.GetRequiredService<IJobStore>().ListAsync(Scope));
        Assert.Equal(submission.JobId, saved.JobId);
        Assert.Equal(submission.PayloadJson, saved.PayloadJson);
        Assert.Equal(message, Assert.Single(await database.ReadOutboxAsync()));
    }

    [Fact]
    public async Task FailedOperation_RollsBackFlushedBusinessAndTask_AndDoesNotLeakPendingOutbox()
    {
        await using var database = await TaskMessagingDatabase.CreateAsync();
        var exportId = Guid.NewGuid();
        var submission = Submission(Guid.NewGuid(), exportId);
        await using var scope = database.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<WorkflowContext>();
        var store = services.GetRequiredService<IJobStore>();
        var unit = services.GetRequiredService<IUnitOfWork>();
        var failure = new InvalidOperationException("The business operation failed after its SQL writes.");
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(async token =>
        {
            context.Exports.Add(new() { Id = exportId, Revision = 1 });
            await store.StageAsync(submission, token);
            await services.GetRequiredService<IIntegrationEventPublisher>().EnqueueAsync(Requested(submission.JobId, exportId), token);
            await context.SaveChangesAsync(token);
            throw failure;
        }));
        Assert.Same(failure, thrown);
        Assert.Empty(await context.Exports.AsNoTracking().ToListAsync());
        Assert.Null(await store.GetAsync(Scope, submission.JobId));
        Assert.Empty(await database.ReadOutboxAsync());

        await unit.ExecuteAsync(async token =>
        {
            context.Exports.Add(new() { Id = exportId, Revision = 2 });
            await store.StageAsync(submission, token);
        });
        Assert.Equal(2, (await context.Exports.SingleAsync()).Revision);
        Assert.NotNull(await store.GetAsync(Scope, submission.JobId));
        Assert.Empty(await database.ReadOutboxAsync());
    }

    [Fact]
    public async Task ReplayedStableJobId_CreatesOneTask_AndConflictingPayloadRollsBackTheEntireOperation()
    {
        await using var database = await TaskMessagingDatabase.CreateAsync();
        var exportId = Guid.NewGuid();
        var submission = Submission(Guid.NewGuid(), exportId);
        for (var revision = 1; revision <= 2; revision++)
        {
            await using var delivery = database.Services.CreateAsyncScope();
            var services = delivery.ServiceProvider;
            var context = services.GetRequiredService<WorkflowContext>();
            await services.GetRequiredService<IUnitOfWork>().ExecuteAsync(async token =>
            {
                var record = await context.Exports.SingleOrDefaultAsync(record => record.Id == exportId, token);
                if (record is null) context.Exports.Add(new() { Id = exportId, Revision = revision });
                else record.Revision = revision;
                await services.GetRequiredService<IJobStore>().StageAsync(submission, token);
                await services.GetRequiredService<IIntegrationEventPublisher>().EnqueueAsync(Requested(submission.JobId, exportId), token);
            });
        }

        await using (var conflict = database.Services.CreateAsyncScope())
        {
            var services = conflict.ServiceProvider;
            var context = services.GetRequiredService<WorkflowContext>();
            await Assert.ThrowsAsync<JobSubmissionConflictException>(() => services.GetRequiredService<IUnitOfWork>().ExecuteAsync(async token =>
            {
                (await context.Exports.SingleAsync(token)).Revision = 3;
                await services.GetRequiredService<IIntegrationEventPublisher>().EnqueueAsync(Requested(submission.JobId, exportId), token);
                await context.SaveChangesAsync(token);
                await services.GetRequiredService<IJobStore>().StageAsync(Submission(submission.JobId, exportId, "pdf"), token);
            }));
        }

        await using var verification = database.Services.CreateAsyncScope();
        var saved = Assert.Single(await verification.ServiceProvider.GetRequiredService<IJobStore>().ListAsync(Scope));
        Assert.Equal(submission.JobId, saved.JobId);
        Assert.Equal("csv", JsonSerializer.Deserialize<ExportPayload>(saved.PayloadJson)!.Format);
        Assert.Equal(2, (await verification.ServiceProvider.GetRequiredService<WorkflowContext>().Exports.SingleAsync()).Revision);
        var messages = await database.ReadOutboxAsync();
        Assert.Equal(2, messages.Count);
        Assert.All(messages, message => Assert.Equal(submission.JobId, message.JobId));
    }

    private static JobSubmission Submission(Guid jobId, Guid exportId, string format = "csv") =>
        JobSubmission.Create(jobId, Scope, "report.export", 1, new ExportPayload(exportId, format));
    private static ExportRequested Requested(Guid jobId, Guid exportId) => new(Guid.NewGuid(), DateTimeOffset.UtcNow, jobId, exportId);
}
