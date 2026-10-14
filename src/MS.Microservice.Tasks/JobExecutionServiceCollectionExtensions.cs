using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MS.Microservice.Tasks;

public static class JobExecutionServiceCollectionExtensions
{
    public static IServiceCollection AddTaskExecution(this IServiceCollection services,
        Action<JobExecutionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(entry => entry.ServiceType == typeof(JobExecutor)))
            throw new InvalidOperationException("Only one task executor can be registered in a service provider.");
        var options = new JobExecutionOptions();
        configure?.Invoke(options);
        if (options.CancellationPollInterval < TimeSpan.FromMilliseconds(25) || options.CancellationPollInterval > TimeSpan.FromMinutes(1)
            || options.StorageOperationTimeout < TimeSpan.FromMilliseconds(25) || options.StorageOperationTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentException("Use finite cancellation polling and storage operation timeouts.", nameof(configure));
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(provider => new JobExecutor(provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<TimeProvider>(), options, provider.GetServices<JobHandlerRegistration>(),
            provider.GetService<ILogger<JobExecutor>>() ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<JobExecutor>.Instance));
        return services;
    }

    /// <summary>Registers an explicit name/version whitelist and a scoped handler; payloads never select CLR types.</summary>
    public static IServiceCollection AddJobHandler<TPayload, THandler>(this IServiceCollection services,
        string name, int version = 1, Action<JobHandlerOptions>? configure = null)
        where THandler : class, IJobHandler<TPayload>
    {
        ArgumentNullException.ThrowIfNull(services);
        JobSubmission.ValidateName(name, 128, nameof(name));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        var options = new JobHandlerOptions();
        configure?.Invoke(options);
        JobSubmission.ValidateName(options.Queue, 64, nameof(options.Queue));
        if (options.Timeout < TimeSpan.FromMilliseconds(1) || options.Timeout > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(configure), "Handler timeouts must be between one millisecond and one day.");
        if (services.Any(entry => entry.ImplementationInstance is JobHandlerRegistration existing
                && existing.Name == name && existing.Version == version))
            throw new InvalidOperationException($"Job handler {name}/{version} is already registered.");
        if (services.Any(entry => entry.ServiceType == typeof(THandler) && entry.Lifetime == ServiceLifetime.Singleton))
            throw new InvalidOperationException("Job handlers must be scoped or transient.");
        var serializerOptions = options.SerializerOptions is null ? null : new JsonSerializerOptions(options.SerializerOptions);
        services.TryAddScoped<THandler>();
        services.AddSingleton(new JobHandlerRegistration(name, version, options.Queue, options.Timeout, options.SafeToRetry,
            (provider, payloadJson, execution, token) =>
            {
                var payload = JsonSerializer.Deserialize<TPayload>(payloadJson, serializerOptions)
                    ?? throw new JsonException("A job payload must not deserialize to null.");
                return provider.GetRequiredService<THandler>().ExecuteAsync(payload, execution, token);
            }));
        return services;
    }
}

internal sealed record JobHandlerRegistration(string Name, int Version, string Queue, TimeSpan Timeout, bool SafeToRetry,
    Func<IServiceProvider, string, JobExecutionContext, CancellationToken, Task<string?>> ExecuteAsync);
