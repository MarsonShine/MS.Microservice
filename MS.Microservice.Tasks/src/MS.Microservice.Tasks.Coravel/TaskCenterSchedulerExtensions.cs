using Coravel.Invocable;
using Coravel.Scheduling.Schedule.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MS.Microservice.Tasks.Coravel;

public static class TaskCenterSchedulerExtensions
{
    /// <summary>Adds task-center startup/shutdown integration without registering a second Coravel scheduler.</summary>
    public static IServiceCollection AddCoravelTaskCenter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!services.Any(entry => entry.ServiceType == typeof(JobExecutor)))
            throw new InvalidOperationException("Call AddTaskExecution before AddCoravelTaskCenter.");
        services.TryAddScoped<TaskCenterTick>();
        services.AddHostedService<TaskCenterLifetime>();
        return services;
    }

    /// <summary>Schedules a short durable-queue dispatch tick on the host's existing Coravel scheduler.</summary>
    public static IScheduledEventConfiguration ScheduleTaskCenter(this IScheduler scheduler, int intervalSeconds = 1)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (intervalSeconds is < 1 or > 59) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
        return scheduler.Schedule<TaskCenterTick>().EverySeconds(intervalSeconds).PreventOverlapping("MS.Microservice.TaskCenter.Tick");
    }
}

internal sealed class TaskCenterTick(JobExecutor executor) : IInvocable
{
    public Task Invoke() => executor.TickAsync();
}

internal sealed class TaskCenterLifetime(JobExecutor executor) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => executor.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => executor.StopAsync(cancellationToken);
}
