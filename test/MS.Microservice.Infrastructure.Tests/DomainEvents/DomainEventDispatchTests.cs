using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MS.Microservice.Domain;
using MS.Microservice.Domain.Aggregates.LogAggregate;
using MS.Microservice.Infrastructure.DbContext;
using MS.Microservice.Infrastructure.Messaging;
using NSubstitute;
using Wolverine;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.DomainEvents;

public sealed class DomainEventDispatchTests
{
    [Fact]
    public async Task WolverineDomainEventDispatcher_ShouldPublishEachDomainEvent()
    {
        var messageBus = Substitute.For<IMessageBus>();
        messageBus.PublishAsync(Arg.Any<IDomainEvent>(), Arg.Any<DeliveryOptions?>()).Returns(ValueTask.CompletedTask);
        var dispatcher = new WolverineDomainEventDispatcher(messageBus);

        await dispatcher.DispatchAsync([new TestDomainEvent("one"), new TestDomainEvent("two")]);

        await messageBus.Received(2).PublishAsync(Arg.Any<IDomainEvent>(), Arg.Any<DeliveryOptions?>());
    }

    [Fact]
    public async Task SaveEntitiesAsync_ShouldSaveBeforeDispatchingAndClearDomainEvents()
    {
        RecordingDomainEventDispatcher? dispatcher = null;
        await using var dbContext = new TestActivationDbContext(() => dispatcher!);
        dispatcher = new RecordingDomainEventDispatcher(() => dbContext.SaveChangesCalled);

        var log = new LogAggregateRoot("event", "method", LogEventTypeEnum.Create, "desc", "content", 1, "127.0.0.1", "13000000000");
        log.AddDomainEvent(new TestDomainEvent("created"));
        dbContext.Add(log);

        await dbContext.SaveEntitiesAsync();

        dbContext.SaveChangesCalled.Should().BeTrue();
        dispatcher.DispatchedEvents.Should().ContainSingle();
        dispatcher.SaveHadCompletedWhenDispatched.Should().BeTrue();
        log.DomainEvents.Should().BeEmpty();
    }

    private sealed record TestDomainEvent(string Name) : IDomainEvent;

    private sealed class RecordingDomainEventDispatcher : IDomainEventDispatcher
    {
        private readonly Func<bool> _saveChangesCalled;

        public RecordingDomainEventDispatcher(Func<bool> saveChangesCalled)
        {
            _saveChangesCalled = saveChangesCalled;
        }

        public List<IDomainEvent> DispatchedEvents { get; } = [];

        public bool SaveHadCompletedWhenDispatched { get; private set; }

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
        {
            SaveHadCompletedWhenDispatched = _saveChangesCalled();
            DispatchedEvents.AddRange(domainEvents);
            return Task.CompletedTask;
        }
    }

    private sealed class DelegatingDomainEventDispatcher : IDomainEventDispatcher
    {
        private readonly Func<IDomainEventDispatcher> _dispatcherAccessor;

        public DelegatingDomainEventDispatcher(Func<IDomainEventDispatcher> dispatcherAccessor)
        {
            _dispatcherAccessor = dispatcherAccessor;
        }

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
        {
            return _dispatcherAccessor().DispatchAsync(domainEvents, cancellationToken);
        }
    }

    private sealed class TestActivationDbContext : ActivationDbContext
    {
        public TestActivationDbContext(Func<IDomainEventDispatcher> dispatcherAccessor)
            : base(
                new DbContextOptionsBuilder<ActivationDbContext>()
                    .UseNpgsql("Host=localhost;Database=activation_test;Username=test;Password=test")
                    .Options,
                Options.Create(new MsPlatformDbContextSettings()),
                new DelegatingDomainEventDispatcher(dispatcherAccessor))
        {
        }

        public bool SaveChangesCalled { get; private set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;
            return Task.FromResult(1);
        }
    }
}
