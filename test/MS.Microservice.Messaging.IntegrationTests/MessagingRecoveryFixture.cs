using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MS.Microservice.Domain.Events;
using MS.Microservice.Infrastructure.Messaging;
using MS.Microservice.Persistence.EFCore.DbContext;
using MS.Microservice.Persistence.EFCore.Inbox;
using MS.Microservice.Persistence.EFCore.Outbox;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Wolverine;
using Wolverine.RabbitMQ;

namespace MS.Microservice.Messaging.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class MessagingRecoveryCollection : ICollectionFixture<MessagingRecoveryFixture>
{
    public const string Name = "messaging-recovery";
}

public sealed class MessagingRecoveryFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("messaging_recovery")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4-alpine")
        .Build();
    private IHost? _wolverineHost;

    public DeliveryRecorder Recorder { get; } = new();

    public IMessageBus MessageBus => _wolverineHost!.Services.GetRequiredService<IMessageBus>();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());
        await using (var context = CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        _wolverineHost = await Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddSingleton(Recorder))
            .UseWolverine(options =>
            {
                options.Discovery.IncludeAssembly(typeof(MessagingRecoveryFixture).Assembly);
                options.UseRabbitMq(new Uri(_rabbitMq.GetConnectionString()))
                    .AutoProvision()
                    .AutoPurgeOnStartup()
                    .UseConventionalRouting();
            })
            .StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_wolverineHost is not null)
        {
            await _wolverineHost.StopAsync();
            _wolverineHost.Dispose();
        }

        await Task.WhenAll(_rabbitMq.DisposeAsync().AsTask(), _postgres.DisposeAsync().AsTask());
    }

    public ActivationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ActivationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__MigrationsHistory", ActivationDbContext.DEFAULT_SCHEMA))
            .Options;
        return new ActivationDbContext(
            options,
            Options.Create(new MsPlatformDbContextSettings()),
            new NoOpDomainEventDispatcher());
    }

    public async Task ResetAsync()
    {
        Recorder.Reset();
        await using var context = CreateDbContext();
        await context.InboxMessages.ExecuteDeleteAsync();
        await context.OutboxMessages.ExecuteDeleteAsync();
        await context.Logs.ExecuteDeleteAsync();
    }
}

public sealed class DeliveryRecorder
{
    private readonly object _lock = new();
    private readonly List<RecoveryIntegrationEvent> _messages = [];
    private TaskCompletionSource _delivered = NewCompletionSource();

    public IReadOnlyList<RecoveryIntegrationEvent> Messages
    {
        get
        {
            lock (_lock)
            {
                return _messages.ToArray();
            }
        }
    }

    public void Record(RecoveryIntegrationEvent message)
    {
        lock (_lock)
        {
            _messages.Add(message);
            _delivered.TrySetResult();
        }
    }

    public async Task WaitAsync(TimeSpan timeout)
        => await _delivered.Task.WaitAsync(timeout);

    public void Reset()
    {
        lock (_lock)
        {
            _messages.Clear();
            _delivered = NewCompletionSource();
        }
    }

    private static TaskCompletionSource NewCompletionSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class RecoveryIntegrationEvent : Core.Messaging.IntegrationEvent
{
    public RecoveryIntegrationEvent()
    {
    }

    public RecoveryIntegrationEvent(string value)
    {
        Value = value;
    }

    public string Value { get; init; } = string.Empty;
}

public static class RecoveryIntegrationEventHandler
{
    public static void Handle(RecoveryIntegrationEvent message, DeliveryRecorder recorder)
        => recorder.Record(message);
}

public sealed class ManualTimeProvider(DateTimeOffset initialUtc) : TimeProvider
{
    private DateTimeOffset _utcNow = initialUtc;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
}
