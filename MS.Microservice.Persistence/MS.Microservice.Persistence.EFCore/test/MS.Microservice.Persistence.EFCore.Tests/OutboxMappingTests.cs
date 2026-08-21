using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MS.Microservice.Domain;
using MS.Microservice.Domain.Events;
using NSubstitute;

namespace MS.Microservice.Persistence.EFCore.Tests;

public sealed class OutboxMappingTests
{
    [Fact]
    public void OutboxMessage_HasRequiredPostgresMappingAndPollingIndexes()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(OutboxMessage));

        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("OutboxMessages");
        entity.GetSchema().Should().Be(ActivationDbContext.DEFAULT_SCHEMA);
        entity.FindProperty(nameof(OutboxMessage.Payload))!.GetColumnType().Should().Be("jsonb");
        entity.FindProperty(nameof(OutboxMessage.ContentType))!.GetMaxLength().Should().Be(100);
        entity.FindProperty(nameof(OutboxMessage.Status))!.GetMaxLength().Should().Be(32);

        var indexPropertySets = entity.GetIndexes()
            .Select(index => index.Properties.Select(property => property.Name).ToArray())
            .ToArray();
        indexPropertySets.Any(properties => properties.SequenceEqual(new[]
        {
            nameof(OutboxMessage.Status),
            nameof(OutboxMessage.NextAttemptAtUtc),
            nameof(OutboxMessage.OccurredAtUtc)
        })).Should().BeTrue();
        indexPropertySets.Any(properties => properties.SequenceEqual(new[]
        {
            nameof(OutboxMessage.LockedUntilUtc)
        })).Should().BeTrue();
    }

    private static ActivationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ActivationDbContext>()
            .UseNpgsql("Host=localhost;Database=outbox_mapping;Username=test;Password=test")
            .Options;
        return new ActivationDbContext(
            options,
            Options.Create(new MsPlatformDbContextSettings()),
            Substitute.For<IDomainEventDispatcher>());
    }
}
