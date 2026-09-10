using System.Reflection;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MS.Microservice.Messaging.RabbitMQ;
using global::Wolverine;
using global::Wolverine.EntityFrameworkCore;
using global::Wolverine.ErrorHandling;
using global::Wolverine.Postgresql;
using global::Wolverine.RabbitMQ;
using global::Wolverine.Runtime;
using RawRabbitMqTransport = MS.Microservice.Messaging.RabbitMQ.RabbitMqTransport;

namespace MS.Microservice.Messaging.Wolverine;

public static class WolverineMessagingExtensions
{
    public static IHostBuilder UseWolverineMessaging<TContext>(this IHostBuilder host,
        MessageTopology topology, WolverineMessagingOptions settings) where TContext : DbContext
    {
        settings.Validate();
        host.ConfigureServices(services => services.AddWolverineMessaging<TContext>(topology, settings));
        return host.UseWolverine(options => ConfigureWolverineMessaging<TContext>(options, topology, settings));
    }

    public static IServiceCollection AddWolverineMessaging<TContext>(this IServiceCollection services,
        MessageTopology topology, WolverineMessagingOptions settings) where TContext : DbContext
    {
        settings.Validate();
        if (services.Any(x => x.ServiceType == typeof(MessagingProviderRegistration)))
            throw new InvalidOperationException("Only one reliable messaging provider can be registered.");
        services.AddSingleton(new MessagingProviderRegistration("Wolverine"));
        services.AddSingleton(topology);
        services.AddSingleton(topology.Registry);
        services.AddSingleton(settings);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<MessagingDiagnostics>();
        services.AddDbContextWithWolverineIntegration<TContext>(options => options.UseNpgsql(settings.ConnectionString,
            postgres => postgres.MigrationsHistoryTable("__MigrationsHistory", settings.Schema)), settings.Schema);
        services.AddScoped(provider => new WolverineUnitOfWork<TContext>(provider.GetRequiredService<TContext>(),
            () => new DbContextOutbox<TContext>(provider.GetRequiredService<IWolverineRuntime>(),
                provider.GetRequiredService<TContext>(), []), topology.Registry));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<WolverineUnitOfWork<TContext>>());
        services.AddScoped<IIntegrationEventPublisher>(provider => provider.GetRequiredService<WolverineUnitOfWork<TContext>>());
        services.AddScoped<IFailedMessageOperations, WolverineFailedMessageOperations>();
        var broker = settings.BrokerOptions();
        foreach (var subscription in topology.Subscriptions)
        {
            broker.Queue(subscription.Consumer);
            services.TryAddScoped(subscription.HandlerType);
        }
        // Sender and provisioning only. No raw receiver or self-managed persistence is registered.
        services.AddSingleton(broker);
        services.AddSingleton<global::RabbitMQ.Client.IConnectionFactory>(_ => broker.CreateConnectionFactory());
        services.AddSingleton<RawRabbitMqTransport>();
        services.AddSingleton<RabbitMqTopologyProvisioner>();
        return services;
    }

    public static void ConfigureWolverineMessaging<TContext>(WolverineOptions options,
        MessageTopology topology, WolverineMessagingOptions settings) where TContext : DbContext
    {
        options.ServiceName = settings.ServiceName;
        options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
        options.DefaultExecutionTimeout = settings.ProcessingTimeout;
        options.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
        options.Durability.KeepAfterMessageHandling = settings.ProcessedRetention;
        options.Durability.DeadLetterQueueExpirationEnabled = false;
        options.PersistMessagesWithPostgresql(settings.ConnectionString, settings.Schema);
        options.UseEntityFrameworkCoreTransactions();
        options.Policies.AutoApplyTransactions();
        options.MetadataRules.Add(new IntegrationEventIdentityRule(topology.Registry));
        options.Transports.Add(new ConfirmedRabbitMqTransport());
        options.UseRabbitMq(new Uri(settings.BrokerConnectionString)).ConfigureChannelCreation(channel =>
        {
            channel.PublisherConfirmationsEnabled = true;
            channel.PublisherConfirmationTrackingEnabled = true;
            channel.ConsumerDispatchConcurrency = 4;
        });
        options.Policies.OnException<MessageContractException>().MoveToErrorQueue();
        options.Policies.OnException<PermanentMessageException>().MoveToErrorQueue();
        var retry = options.Policies.OnException<Exception>(exception => exception is not MessageContractException
            and not PermanentMessageException and not OperationCanceledException);
        if (settings.MaxRetryAttempts == 0) retry.MoveToErrorQueue();
        else retry.RetryWithCooldown(Enumerable.Range(0, settings.MaxRetryAttempts)
            .Select(index => TimeSpan.FromSeconds(Math.Min(900, 5 * Math.Pow(2, index)))).ToArray()).Then.MoveToErrorQueue();
        foreach (var contract in topology.Registry.Contracts)
        {
            typeof(WolverineMessagingExtensions).GetMethod(nameof(ConfigureContract), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(contract.MessageType, typeof(TContext)).Invoke(null, [options, contract]);
        }
        foreach (var subscription in topology.Subscriptions)
        {
            options.ListenToRabbitQueue(settings.BrokerOptions().Queue(subscription.Consumer))
                .Named(subscription.Consumer).UseDurableInbox();
        }
    }

    private static void ConfigureContract<TEvent, TContext>(WolverineOptions options, MessageContract contract)
        where TEvent : IIntegrationEvent where TContext : DbContext
    {
        var alias = $"{contract.Name}.v{contract.Version}";
        options.RegisterMessageType(typeof(TEvent), alias);
        options.Discovery.IncludeType(typeof(WolverineIntegrationEventHandler<TEvent, TContext>));
        options.PublishMessage<TEvent>().To(new Uri($"{ConfirmedRabbitMqTransport.Scheme}://exchange/{Uri.EscapeDataString(alias)}"))
            .UseDurableOutbox();
    }
}
