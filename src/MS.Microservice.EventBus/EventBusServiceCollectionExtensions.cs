using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MS.Microservice.EventBus.Abstractions;

namespace MS.Microservice.EventBus;

public static class EventBusServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers the in-process bus and its shared subscription manager. Register handler types separately.</summary>
        public IServiceCollection AddInMemoryEventBus()
        {
            services.TryAddSingleton<IEventBusSubscriptionsManager>(sp =>
                sp.GetService<InMemoryEventBusSubscriptionsManager>() ?? new InMemoryEventBusSubscriptionsManager());
            services.TryAddSingleton(sp => new InMemoryEventBus(
                sp.GetRequiredService<IEventBusSubscriptionsManager>() as InMemoryEventBusSubscriptionsManager
                    ?? throw new InvalidOperationException("InMemoryEventBus requires an InMemoryEventBusSubscriptionsManager."),
                sp.GetRequiredService<IServiceScopeFactory>()));
            services.TryAddSingleton<IEventbus>(sp => sp.GetRequiredService<InMemoryEventBus>());
            services.TryAddSingleton<IEventBus>(sp => sp.GetRequiredService<IEventbus>());
            return services;
        }
    }
}
