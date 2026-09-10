using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Messaging;
using MS.Microservice.Messaging.SelfManaged;

public static class MessagingConsumer
{
    public static async Task VerifyAsync()
    {
        await VerifyContextAsync<FirstContext>();
        await VerifyContextAsync<SecondContext>();
        Console.WriteLine("Two independent business contexts passed commit, rollback and duplicate-consumption checks.");
    }

    private static async Task VerifyContextAsync<TContext>() where TContext : ConsumerContext
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var topology = new MessageTopology([MessageContract.For<ProfileChanged>("consumer.profile.changed", 1)],
            [MessageSubscription.For<ProfileChanged, AuditHandler<TContext>>("audit")]);
        var services = new ServiceCollection();
        services.AddDbContext<TContext>(options => options.UseSqlite(connection));
        services.AddSelfManagedMessaging<TContext>(topology);
        await using var provider = services.BuildServiceProvider();
        var message = new ProfileChanged(Guid.NewGuid(), DateTimeOffset.UtcNow, "中文档案", 12.34m);
        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await context.Database.EnsureCreatedAsync();
            var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
            await unit.ExecuteAsync(async token =>
            {
                context.Add(new ProfileRow { Name = message.Name });
                await publisher.EnqueueAsync(message, token);
            });
            try
            {
                await unit.ExecuteAsync(async token =>
                {
                    context.Add(new ProfileRow { Name = "must roll back" });
                    await publisher.EnqueueAsync(message with { Id = Guid.NewGuid() }, token);
                    throw new InvalidOperationException("intentional rollback");
                });
                throw new Exception("The failing operation unexpectedly committed.");
            }
            catch (InvalidOperationException exception) when (exception.Message == "intentional rollback") { }
            if (await context.Set<ProfileRow>().CountAsync() != 1) throw new Exception("Business rollback failed.");
        }
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Payload FROM Outbox";
            var payload = (string)(await command.ExecuteScalarAsync())!;
            var persisted = System.Text.Json.JsonSerializer.Deserialize<ProfileChanged>(payload,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            if (persisted != message) throw new Exception("Persisted message identity or data changed.");
            command.CommandText = "SELECT COUNT(*) FROM Outbox";
            if (Convert.ToInt64(await command.ExecuteScalarAsync()) != 1) throw new Exception("Outgoing rollback failed.");
        }
        var receiver = provider.GetRequiredService<IMessageReceiver>();
        var serialized = topology.Registry.Serialize(message);
        for (var index = 0; index < 2; index++)
            if (await receiver.ReceiveAsync(serialized, "audit", default) != DeliveryResult.Acknowledge)
                throw new Exception("Consumption was not acknowledged.");
        using var check = provider.CreateScope();
        if (await check.ServiceProvider.GetRequiredService<TContext>().Set<AuditRow>().CountAsync() != 1)
            throw new Exception("Duplicate business effect.");
    }
}

public abstract class ConsumerContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<ProfileRow>().HasKey(row => row.Id);
        builder.Entity<AuditRow>().HasKey(row => row.Id);
        builder.AddSelfManagedMessaging();
    }
}
public sealed class FirstContext(DbContextOptions<FirstContext> options) : ConsumerContext(options);
public sealed class SecondContext(DbContextOptions<SecondContext> options) : ConsumerContext(options);
public sealed class ProfileRow { public int Id { get; set; } public string Name { get; set; } = ""; }
public sealed class AuditRow { public Guid Id { get; set; } public string Name { get; set; } = ""; }
public sealed record ProfileChanged(Guid Id, DateTimeOffset OccurredAtUtc, string Name, decimal Amount) : IIntegrationEvent;
public sealed class AuditHandler<TContext>(TContext context) : IIntegrationEventHandler<ProfileChanged> where TContext : ConsumerContext
{
    public Task HandleAsync(ProfileChanged message, MessageContext metadata, CancellationToken token)
    {
        context.Add(new AuditRow { Id = message.Id, Name = message.Name });
        return Task.CompletedTask;
    }
}
