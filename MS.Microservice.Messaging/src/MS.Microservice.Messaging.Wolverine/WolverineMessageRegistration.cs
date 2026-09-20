using Microsoft.EntityFrameworkCore;
using global::Wolverine;

namespace MS.Microservice.Messaging.Wolverine;

/// <summary>Roots the event and database-specific bridge through a closed generic registration.</summary>
public sealed class WolverineMessageRegistration<TContext> where TContext : DbContext
{
    private readonly Action<WolverineOptions, MessageContract> _configure;

    private WolverineMessageRegistration(Type messageType, Action<WolverineOptions, MessageContract> configure)
        => (MessageType, _configure) = (messageType, configure);

    public Type MessageType { get; }

    public static WolverineMessageRegistration<TContext> For<TEvent>() where TEvent : IIntegrationEvent
        => new(typeof(TEvent), static (options, contract) =>
        {
            var alias = $"{contract.Name}.v{contract.Version}";
            options.RegisterMessageType(typeof(TEvent), alias);
            options.Discovery.IncludeType(typeof(WolverineIntegrationEventHandler<TEvent, TContext>));
            options.PublishMessage<TEvent>()
                .To(new Uri($"{ConfirmedRabbitMqTransport.Scheme}://exchange/{Uri.EscapeDataString(alias)}"))
                .UseDurableOutbox();
        });

    internal void Configure(WolverineOptions options, MessageContract contract) => _configure(options, contract);
}
