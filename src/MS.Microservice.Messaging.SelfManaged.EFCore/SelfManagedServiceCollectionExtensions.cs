using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MS.Microservice.Messaging.SelfManaged;

public static class SelfManagedServiceCollectionExtensions
{
    public static IServiceCollection AddSelfManagedMessaging<TContext>(this IServiceCollection services,
        MessageTopology topology, Action<SelfManagedOptions>? configure = null) where TContext : DbContext
    {
        if (services.Any(x => x.ServiceType == typeof(MessagingProviderRegistration)))
            throw new InvalidOperationException("Only one reliable messaging provider can be registered.");
        var options = new SelfManagedOptions();
        configure?.Invoke(options);
        options.Validate();
        services.AddSingleton(new MessagingProviderRegistration(MessagingProvider.SelfManaged));
        services.AddSingleton(topology);
        services.AddSingleton(topology.Registry);
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddLogging();
        services.TryAddSingleton<MessagingDiagnostics>();
        services.AddScoped<SelfManagedPublisher<TContext>>();
        services.AddHostedService<SelfManagedOutboxWorker<TContext>>();
        services.AddScoped<SelfManagedUnitOfWork<TContext>>();
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<SelfManagedUnitOfWork<TContext>>());
        services.AddScoped<IIntegrationEventPublisher>(provider => provider.GetRequiredService<SelfManagedUnitOfWork<TContext>>());
        services.AddScoped<OutboxStore<TContext>>();
        services.AddScoped<InboxStore<TContext>>();
        services.AddScoped<IFailedMessageOperations, SelfManagedFailedMessageOperations<TContext>>();
        services.AddSingleton<IMessageReceiver, SelfManagedReceiver<TContext>>();
        foreach (var subscription in topology.Subscriptions) services.TryAddScoped(subscription.HandlerType);
        return services;
    }
}
