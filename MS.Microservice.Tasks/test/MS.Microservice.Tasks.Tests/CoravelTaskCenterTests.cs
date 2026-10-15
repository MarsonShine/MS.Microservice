using Coravel;
using Coravel.Scheduling.Schedule.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MS.Microservice.Tasks.Coravel;
using MS.Microservice.Tasks.EFCore;
using Xunit;

namespace MS.Microservice.Tasks.Tests;

public sealed class CoravelTaskCenterTests
{
    [Fact]
    public async Task ExistingCoravelScheduler_DispatchesDurableJobs_WithoutAddingAnotherScheduler()
    {
        await using var database = await JobDatabase.CreateAsync();
        var probe = new HandlerProbe();
        using var host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddDbContext<JobTestContext>(options => options.UseSqlite(database.ConnectionString));
            services.AddEfCoreJobStore<JobTestContext>();
            services.AddSingleton(probe);
            services.AddTaskExecution();
            services.AddJobHandler<ControlledPayload, ControlledHandler>("scheduled");
            services.AddScheduler();
            services.AddCoravelTaskCenter();
        }).Build();
        host.Services.UseScheduler(scheduler => scheduler.ScheduleTaskCenter());
        Assert.Throws<ArgumentOutOfRangeException>(() => host.Services.GetRequiredService<IScheduler>().ScheduleTaskCenter(60));
        Assert.Single(host.Services.GetServices<IHostedService>(),
            service => service.GetType().Assembly == typeof(IScheduler).Assembly);
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var job = await store.SubmitAsync(JobSubmission.Create(Guid.NewGuid(), "owner", "scheduled", 1,
            new ControlledPayload("coravel")));
        await host.StartAsync();
        await probe.For("coravel").CleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while ((await store.GetAsync("owner", job.JobId, completion.Token))!.State == JobState.Running)
            await Task.Delay(10, completion.Token);
        await host.StopAsync();
        var completed = Assert.IsType<JobSnapshot>(await store.GetAsync("owner", job.JobId));
        Assert.Equal(JobState.Succeeded, completed.State);
        Assert.Equal("private:coravel", completed.ResultReference);
    }
}
