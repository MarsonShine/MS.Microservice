using Microsoft.EntityFrameworkCore;
using MS.Microservice.Messaging;
using MS.Microservice.Messaging.Wolverine;
using global::Wolverine;

namespace MS.Microservice.Lab.AotExamples.Static.Messaging;

/// <summary>Independent static form of the same alias, bridge and durable route registration.</summary>
public static class WolverineBridgeExample
{
    public static void Configure<TEvent, TContext>(WolverineOptions options,
        global::MS.Microservice.Messaging.MessageContractRegistry contracts)
        where TEvent : IIntegrationEvent where TContext : DbContext
    {
        var contract = contracts.Get(typeof(TEvent));
        var alias = $"{contract.Name}.v{contract.Version}";
        options.RegisterMessageType(typeof(TEvent), alias);
        options.Discovery.IncludeType(typeof(WolverineIntegrationEventHandler<TEvent, TContext>));
        options.PublishMessage<TEvent>().To(new Uri($"{ConfirmedRabbitMqTransport.Scheme}://exchange/{Uri.EscapeDataString(alias)}"))
            .UseDurableOutbox();
    }
}
