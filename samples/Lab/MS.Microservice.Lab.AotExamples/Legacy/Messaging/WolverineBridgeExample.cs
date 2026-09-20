using System.Reflection;
using Microsoft.EntityFrameworkCore;
using MS.Microservice.Messaging;
using MS.Microservice.Messaging.Wolverine;
using global::Wolverine;

namespace MS.Microservice.Lab.AotExamples.Legacy.Messaging;

/// <summary>The original bridge-registration loop extracted from the archived complete extension.</summary>
public static class WolverineBridgeExample
{
    public static void Configure<TContext>(WolverineOptions options,
        global::MS.Microservice.Messaging.MessageContractRegistry contracts) where TContext : DbContext
    {
        foreach (var contract in contracts.Contracts)
        {
            typeof(WolverineBridgeExample).GetMethod(nameof(ConfigureContract), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(contract.MessageType, typeof(TContext)).Invoke(null, [options, contract]);
        }
    }

    private static void ConfigureContract<TEvent, TContext>(WolverineOptions options,
        global::MS.Microservice.Messaging.MessageContract contract)
        where TEvent : IIntegrationEvent where TContext : DbContext
    {
        var alias = $"{contract.Name}.v{contract.Version}";
        options.RegisterMessageType(typeof(TEvent), alias);
        options.Discovery.IncludeType(typeof(WolverineIntegrationEventHandler<TEvent, TContext>));
        options.PublishMessage<TEvent>().To(new Uri($"{ConfirmedRabbitMqTransport.Scheme}://exchange/{Uri.EscapeDataString(alias)}"))
            .UseDurableOutbox();
    }
}
