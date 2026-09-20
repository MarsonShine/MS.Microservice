using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MS.Microservice.Messaging.FaultWorker;
using MS.Microservice.Messaging.RabbitMQ;
using MS.Microservice.Messaging.SelfManaged;
using MS.Microservice.Messaging.Wolverine;
using global::Wolverine.EntityFrameworkCore;
using global::Wolverine.Runtime;

namespace MS.Microservice.Messaging.IntegrationTests;

public enum ProbeKind { Normal, Nested, Empty }
public sealed record ProbeDetail(string Text, int Number);
public sealed record ProbeEvent(Guid Id, DateTimeOffset OccurredAtUtc, string Title, decimal Amount,
    ProbeKind Kind, ProbeDetail? Detail, IReadOnlyList<string?> Labels, Guid FollowUpId, int Depth = 0) : IIntegrationEvent;
public sealed class ProbeReceipt
{
    public Guid MessageId { get; set; }
    public string Consumer { get; set; } = "";
    public string Payload { get; set; } = "";
}
public abstract class ProbeContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<ProbeReceipt> Receipts => Set<ProbeReceipt>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ProbeReceipt>().ToTable("Receipts", "probe");
        model.Entity<ProbeReceipt>().HasKey(x => new { x.MessageId, x.Consumer });
    }
}
public sealed class SelfProbeContext(DbContextOptions<SelfProbeContext> options) : ProbeContext(options)
{
    protected override void OnModelCreating(ModelBuilder model) { base.OnModelCreating(model); model.AddSelfManagedMessaging(); }
}
public sealed class NativeProbeContext(DbContextOptions<NativeProbeContext> options) : ProbeContext(options)
{
    protected override void OnModelCreating(ModelBuilder model) { base.OnModelCreating(model); model.MapWolverineEnvelopeStorage("wolverine"); }
}
public sealed class ProbeBehavior
{
    public Guid FailingId { get; set; }
    public bool EmitFollowUp { get; set; }
    public int TransientFailures { get; set; }
    public int Attempts;
}
public sealed class ProbeHandler(ProbeContext context, MessageContractRegistry registry,
    IIntegrationEventPublisher publisher, ProbeBehavior behavior) : IIntegrationEventHandler<ProbeEvent>
{
    public async Task HandleAsync(ProbeEvent message, MessageContext metadata, CancellationToken token)
    {
        context.Receipts.Add(new() { MessageId = message.Id, Consumer = metadata.Consumer, Payload = registry.Serialize(message).Payload });
        if (message.Depth == 0 && behavior.EmitFollowUp)
            await publisher.EnqueueAsync(message with { Id = message.FollowUpId, Depth = 1 }, token);
        if (message.Id == behavior.FailingId)
        {
            if (behavior.TransientFailures == 0) throw new PermanentMessageException("probe_poison");
            if (Interlocked.Increment(ref behavior.Attempts) <= behavior.TransientFailures) throw new InvalidOperationException("probe_transient");
        }
    }
}

internal static class ProbeHost
{
    public static MessageTopology Topology(bool twoConsumers = false) => new([MessageContract.For<ProbeEvent>("probe.changed", ProbeJsonContext.Default.ProbeEvent)],
        twoConsumers ? [MessageSubscription.For<ProbeEvent, ProbeHandler>("primary"), MessageSubscription.For<ProbeEvent, ProbeHandler>("secondary")]
            : [MessageSubscription.For<ProbeEvent, ProbeHandler>("primary")]);

    public static async Task<IHost> CreateAsync(FaultEnvironment environment, ProbeBehavior behavior, bool twoConsumers = false)
    {
        var topology = Topology(twoConsumers);
        var builder = Host.CreateDefaultBuilder().ConfigureLogging(logging => logging.ClearProviders().AddSimpleConsole());
        builder.ConfigureServices(services => services.AddSingleton(behavior));
        if (environment.Provider == "SelfManaged")
        {
            builder.ConfigureServices(services =>
            {
                services.AddDbContext<SelfProbeContext>(options => options.UseNpgsql(environment.ConnectionString));
                services.AddScoped<ProbeContext>(provider => provider.GetRequiredService<SelfProbeContext>());
                services.AddSelfManagedMessaging<SelfProbeContext>(topology, options =>
                {
                    options.PollInterval = TimeSpan.FromMilliseconds(100);
                    options.InitialRetryDelay = TimeSpan.FromMilliseconds(100);
                    options.MaxRetryAttempts = 2;
                });
                services.AddRabbitMqTransport(environment.Broker);
            });
        }
        else
        {
            builder.UseWolverineMessaging<NativeProbeContext>(topology, new()
            {
                ConnectionString = environment.ConnectionString, BrokerConnectionString = environment.BrokerConnectionString,
                Exchange = environment.Prefix, QueuePrefix = environment.Prefix, MaxRetryAttempts = 2
            }, [WolverineMessageRegistration<NativeProbeContext>.For<ProbeEvent>()]);
            builder.ConfigureServices(services => services.AddScoped<ProbeContext>(provider => provider.GetRequiredService<NativeProbeContext>()));
        }
        var host = builder.Build();
        try
        {
            using var scope = host.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ProbeContext>();
            await context.Database.EnsureCreatedAsync();
            if (environment.Provider == "Wolverine")
            {
                var database = (Weasel.Core.Migrations.IDatabase)host.Services.GetRequiredService<IWolverineRuntime>().Storage;
                var file = Path.GetTempFileName();
                try
                {
                    await database.WriteCreationScriptToFileAsync(file);
                    await context.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(file));
                }
                finally { File.Delete(file); }
            }
            await host.Services.GetRequiredService<RabbitMqTopologyProvisioner>().ProvisionAsync();
            await host.StartAsync();
            return host;
        }
        catch { host.Dispose(); throw; }
    }

    public static async Task EnqueueAsync(IHost host, params ProbeEvent[] messages)
    {
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteAsync(async token =>
        {
            foreach (var message in messages)
                await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().EnqueueAsync(message, token);
        });
    }

    public static Task WaitForReceiptsAsync(IHost host, int count) => MessagingRecoveryFixture.UntilAsync(async token =>
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ProbeContext>().Receipts.CountAsync(token) == count;
    });

    public static ProbeEvent Message(int variant = 0) => new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddTicks(7),
        variant switch { 1 => "", 2 => "标点：%&? café", _ => "中文构造器属性" },
        variant == 1 ? 0 : -1234.5678m, variant == 2 ? ProbeKind.Nested : ProbeKind.Normal,
        variant == 1 ? null : new("嵌套", int.MaxValue), ["中文", null, ""], Guid.NewGuid());

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ProbeEvent))]
internal partial class ProbeJsonContext : JsonSerializerContext;
