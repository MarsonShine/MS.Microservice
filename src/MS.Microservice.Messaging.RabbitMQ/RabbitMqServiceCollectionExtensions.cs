using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace MS.Microservice.Messaging.RabbitMQ;

public static class RabbitMqServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqTransport(this IServiceCollection services, RabbitMqOptions options)
    {
        options.Validate();
        if (services.Any(x => x.ServiceType == typeof(IMessageTransport)))
            throw new InvalidOperationException("A message transport is already registered.");
        services.AddSingleton(options);
        services.AddSingleton<IConnectionFactory>(_ => options.CreateConnectionFactory());
        services.AddSingleton<RabbitMqTransport>();
        services.AddSingleton<IMessageTransport>(provider => provider.GetRequiredService<RabbitMqTransport>());
        services.AddSingleton<RabbitMqTopologyProvisioner>();
        services.AddSingleton<RabbitMqDeliveryHandler>();
        services.AddHostedService<RabbitMqConsumerService>();
        return services;
    }
}
