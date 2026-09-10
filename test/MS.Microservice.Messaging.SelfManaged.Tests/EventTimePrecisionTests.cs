using Microsoft.EntityFrameworkCore;
using MS.Microservice.Messaging.SelfManaged;
using Xunit;
using static MS.Microservice.Messaging.SelfManaged.Tests.UnitOfWorkTests;

namespace MS.Microservice.Messaging.SelfManaged.Tests;

public sealed class EventTimePrecisionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(9)]
    public async Task EventIdentityUsesLosslessStorageInsteadOfDatabaseTimestampPrecision(int subMicrosecondTicks)
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var occurredAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(subMicrosecondTicks);
        var message = Registry().Serialize(NewEvent() with { OccurredAtUtc = occurredAt });
        context.Add(OutboxEntry.From(message, DateTime.UtcNow));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var persisted = (await context.Set<OutboxEntry>().SingleAsync()).ToMessage();
        Assert.Equal(occurredAt, persisted.OccurredAtUtc);
        Assert.Equal(message.Id, Registry().Deserialize(persisted).Id);
        var inbox = new InboxStore<BusinessContext>(context, new(), TimeProvider.System);
        await inbox.AcquireAsync(message, "precision", Guid.NewGuid(), default);
        Assert.Equal(message, (await inbox.FindAsync(message.Id, "precision", default))!.ToMessage());
        await using var command = connection.CreateCommand();
        command.CommandText = "select OccurredAtUtc from Outbox";
        Assert.Equal(occurredAt.UtcTicks, Assert.IsType<long>(await command.ExecuteScalarAsync()));
    }
}
