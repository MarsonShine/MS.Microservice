using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MS.Microservice.Messaging.RabbitMQ;
using MS.Microservice.Messaging.SelfManaged;
using MS.Microservice.Messaging.Wolverine;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Persistence;
using global::Wolverine;

namespace MS.Microservice.Messaging.FaultWorker;

public sealed record FaultEnvironment(string Provider, string ConnectionString, string BrokerConnectionString, string Prefix)
{
    public RabbitMqOptions Broker => new() { ConnectionString = BrokerConnectionString, Exchange = Prefix, QueuePrefix = Prefix };
}

public static class FaultHost
{
    public static IHost Build(FaultEnvironment environment, string? phase = null)
    {
        var barrier = new FaultBarrier(phase);
        var topology = ReferenceMessages.Topology();
        var builder = Host.CreateDefaultBuilder().ConfigureLogging(logging => logging.ClearProviders().AddSimpleConsole());
        if (environment.Provider == "SelfManaged")
        {
            builder.ConfigureServices(services =>
            {
                services.AddDbContext<SelfManagedReferenceDbContext>(options => options.UseNpgsql(environment.ConnectionString,
                    pg => pg.MigrationsHistoryTable("__MigrationsHistory", ReferenceDbContext.Schema))
                    .AddInterceptors(new SaveBarrier(barrier), new CommitBarrier(barrier)));
                services.AddReferenceRepositories<SelfManagedReferenceDbContext>();
                services.AddSelfManagedMessaging<SelfManagedReferenceDbContext>(topology, options =>
                {
                    options.PollInterval = TimeSpan.FromMilliseconds(100);
                    options.PublishingLease = options.ProcessingLease = TimeSpan.FromSeconds(3);
                    options.InitialRetryDelay = TimeSpan.FromMilliseconds(200);
                    options.ProcessingTimeout = TimeSpan.FromSeconds(30);
                });
                services.AddRabbitMqTransport(environment.Broker);
            });
        }
        else if (environment.Provider == "Wolverine")
        {
            var settings = new WolverineMessagingOptions
            {
                ConnectionString = environment.ConnectionString, BrokerConnectionString = environment.BrokerConnectionString,
                Exchange = environment.Prefix, QueuePrefix = environment.Prefix, ServiceName = environment.Prefix
            };
            builder.ConfigureServices(services =>
            {
                services.AddWolverineMessaging<WolverineReferenceDbContext>(topology, settings);
                services.AddDbContext<WolverineReferenceDbContext>(options =>
                    options.AddInterceptors(new SaveBarrier(barrier), new CommitBarrier(barrier)));
                services.AddReferenceRepositories<WolverineReferenceDbContext>();
            });
            builder.UseWolverine(options =>
            {
                WolverineMessagingExtensions.ConfigureWolverineMessaging<WolverineReferenceDbContext>(options, topology, settings);
                options.Durability.HealthCheckPollingTime = TimeSpan.FromSeconds(1);
                options.Durability.FirstHealthCheckExecution = TimeSpan.FromSeconds(1);
                options.Durability.NodeReassignmentPollingTime = TimeSpan.FromSeconds(1);
                options.Durability.FirstNodeReassignmentExecution = TimeSpan.FromSeconds(1);
                options.Durability.StaleNodeTimeout = TimeSpan.FromSeconds(5);
                options.Durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(1);
            });
        }
        else throw new ArgumentException("Unknown test provider.");
        return builder.Build();
    }
}
