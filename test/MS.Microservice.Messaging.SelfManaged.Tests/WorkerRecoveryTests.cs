using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MS.Microservice.Messaging.SelfManaged;
using Xunit;
using static MS.Microservice.Messaging.SelfManaged.Tests.UnitOfWorkTests;

namespace MS.Microservice.Messaging.SelfManaged.Tests;

public sealed class WorkerRecoveryTests
{
    [Fact]
    public async Task WorkerRecoversAfterStorageBecomesAvailable()
    {
        var connectionString = $"Data Source=recovery-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var unavailable = new SignalLogger<SelfManagedOutboxWorker<WorkerContext>>();
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddScoped(_ => new WorkerContext(connectionString));
        services.AddSingleton<ILogger<SelfManagedOutboxWorker<WorkerContext>>>(unavailable);
        services.AddSingleton<IMessageTransport>(new PublisherTests.Transport("confirmed", () => delivered.TrySetResult()));
        services.AddSelfManagedMessaging<WorkerContext>(new([MessageContract.For<Changed>("profile.changed")], []), options =>
        {
            options.InitialRetryDelay = TimeSpan.FromMilliseconds(50);
            options.PollInterval = TimeSpan.FromMilliseconds(20);
        });
        await using var provider = services.BuildServiceProvider();
        var worker = Assert.Single(provider.GetServices<IHostedService>());
        await worker.StartAsync(default);
        try
        {
            await unavailable.Warning.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<WorkerContext>().Database.EnsureCreatedAsync();
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteAsync(async token =>
                    await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().EnqueueAsync(NewEvent(), token));
            }
            await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(timeout.Token);
        }
    }

    private sealed class WorkerContext(string connectionString) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connectionString);
        protected override void OnModelCreating(ModelBuilder model) => model.AddSelfManagedMessaging();
    }

    private sealed class SignalLogger<T> : ILogger<T>
    {
        public TaskCompletionSource Warning { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (level == LogLevel.Warning) Warning.TrySetResult();
        }
    }
}
