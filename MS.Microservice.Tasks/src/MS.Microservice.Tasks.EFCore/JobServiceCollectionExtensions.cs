using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MS.Microservice.Tasks.EFCore;

public static class JobServiceCollectionExtensions
{
    /// <summary>Uses the host's scoped DbContext; independent operations resolve a fresh scope of the same type.</summary>
    public static IServiceCollection AddEfCoreJobStore<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(service => service.ServiceType == typeof(IJobStore)))
            throw new InvalidOperationException("Only one job store can be registered in a service provider.");
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<EfCoreJobStore<TContext>>();
        services.AddScoped<IJobStore>(provider => provider.GetRequiredService<EfCoreJobStore<TContext>>());
        services.AddScoped<IJobRetentionStore>(provider => provider.GetRequiredService<EfCoreJobStore<TContext>>());
        return services;
    }
}
