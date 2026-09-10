using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Messaging.SelfManaged;
using Xunit;
using static MS.Microservice.Messaging.SelfManaged.Tests.UnitOfWorkTests;

namespace MS.Microservice.Messaging.SelfManaged.Tests;

public sealed class PublisherTests
{
    [Theory]
    [InlineData("confirmed", OutboxState.Published)]
    [InlineData("unconfirmed", OutboxState.Pending)]
    [InlineData("unroutable", OutboxState.DeadLettered)]
    internal async Task OnlyPositiveConfirmationPublishes(string result, OutboxState expected)
    {
        await using var connection = await OpenAsync();
        var services = new ServiceCollection();
        services.AddScoped(_ => new BusinessContext(connection));
        var transport = new Transport(result);
        services.AddSingleton<IMessageTransport>(transport);
        services.AddSelfManagedMessaging<BusinessContext>(ReceiverTests.Topology());
        await using var provider = services.BuildServiceProvider();
        var message = NewEvent();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<BusinessContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteAsync(async token =>
                await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().EnqueueAsync(message, token));
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var count = await scope.ServiceProvider.GetRequiredService<SelfManagedPublisher<BusinessContext>>()
                .PublishBatchAsync(default);
            Assert.Equal(expected == OutboxState.Published ? 1 : 0, count);
        }
        await using var check = provider.CreateAsyncScope();
        var entry = await check.ServiceProvider.GetRequiredService<BusinessContext>().Set<OutboxEntry>().SingleAsync();
        Assert.Equal(expected, entry.State);
        Assert.Equal(message.Id, Assert.Single(transport.Messages).Id);
        Assert.Equal(message, Registry().Deserialize(entry.ToMessage()));
    }

    [Fact]
    public async Task ConfirmationAfterLeaseExpiryDoesNotReportSuccess()
    {
        await using var connection = await OpenAsync();
        var services = new ServiceCollection();
        services.AddScoped(_ => new BusinessContext(connection));
        var clock = new OutboxStoreTests.Clock();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IMessageTransport>(new Transport("confirmed", () => clock.Advance(TimeSpan.FromMinutes(2))));
        services.AddSelfManagedMessaging<BusinessContext>(ReceiverTests.Topology());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BusinessContext>();
        await context.Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteAsync(async token =>
            await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().EnqueueAsync(NewEvent(), token));
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<SelfManagedPublisher<BusinessContext>>().PublishBatchAsync(default));
        Assert.Equal(OutboxState.Publishing, (await context.Set<OutboxEntry>().AsNoTracking().SingleAsync()).State);
    }

    internal sealed class Transport(string outcome, Action? beforeResult = null) : IMessageTransport
    {
        public List<SerializedMessage> Messages { get; } = [];
        public Task SendConfirmedAsync(SerializedMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(message);
            beforeResult?.Invoke();
            return outcome switch
            {
                "unconfirmed" => Task.FromException(new IOException("confirmation unavailable")),
                "unroutable" => Task.FromException(new PermanentMessageException("unroutable")),
                _ => Task.CompletedTask
            };
        }
    }
}
