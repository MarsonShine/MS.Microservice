using MS.Microservice.Messaging;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Domain;
using NSubstitute;
using Xunit;

namespace MS.Microservice.Reference.Application.Tests;

public sealed class ProfileServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DomainEventsAreClearedOnlyAfterSuccessfulCommit(bool failCommit)
    {
        var repository = Substitute.For<IProfileRepository>();
        UserProfile? added = null;
        repository.When(x => x.Add(Arg.Any<UserProfile>())).Do(call => added = call.Arg<UserProfile>());
        var unit = new Unit(failCommit);
        var publisher = Substitute.For<IIntegrationEventPublisher>();
        var service = new ProfileService(repository, unit, publisher, TimeProvider.System);
        var action = () => service.CreateAsync(new("https://issuer.example", "subject", "name", ["reader"]), new("https://issuer.example", "admin"));
        if (failCommit) await Assert.ThrowsAsync<IOException>(action);
        else Assert.True((await action()).IsRight);
        Assert.NotNull(added);
        Assert.Equal(failCommit ? 1 : 0, added.DomainEvents.Count);
        await publisher.Received(1).EnqueueAsync(Arg.Is<IIntegrationEvent>(message =>
            message is UserProfileChangedV1 && message.Id != Guid.Empty), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateIdentityDoesNotPublishAnotherChange()
    {
        var repository = Substitute.For<IProfileRepository>();
        repository.FindAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UserProfile.Create("https://issuer.example", "subject", "name", [], DateTimeOffset.UtcNow));
        var publisher = Substitute.For<IIntegrationEventPublisher>();
        var service = new ProfileService(repository, new Unit(), publisher, TimeProvider.System);
        var result = await service.CreateAsync(new("https://issuer.example", "subject", "name", []), new("https://issuer.example", "admin"));
        Assert.Equal("conflict", result.Left.Code);
        await publisher.DidNotReceive().EnqueueAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StaleVersionCannotChangeOrPublish()
    {
        var repository = Substitute.For<IProfileRepository>();
        var profile = UserProfile.Create("https://issuer.example", "subject", "name", [], DateTimeOffset.UtcNow);
        profile.ClearDomainEvents();
        repository.GetAsync(profile.Id, Arg.Any<CancellationToken>()).Returns(profile);
        var publisher = Substitute.For<IIntegrationEventPublisher>();
        var service = new ProfileService(repository, new Unit(), publisher, TimeProvider.System);
        var result = await service.ChangeAsync(profile.Id, new("changed", [], 2), new("https://issuer.example", "admin"));
        Assert.Equal("conflict", result.Left.Code);
        Assert.Equal("name", profile.DisplayName);
        await publisher.DidNotReceive().EnqueueAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangedProfilePreservesResultAndClearsEventsAfterCommit()
    {
        var repository = Substitute.For<IProfileRepository>();
        var profile = UserProfile.Create("https://issuer.example", "subject", "name", [], DateTimeOffset.UtcNow);
        profile.ClearDomainEvents();
        repository.GetAsync(profile.Id, Arg.Any<CancellationToken>()).Returns(profile);
        var publisher = Substitute.For<IIntegrationEventPublisher>();
        var service = new ProfileService(repository, new Unit(), publisher, TimeProvider.System);

        var result = await service.ChangeAsync(profile.Id, new("changed", ["reader"], 1), new("https://issuer.example", "admin"));

        Assert.True(result.IsRight);
        Assert.Equal(profile.Id, result.Right.Id);
        Assert.Equal("changed", result.Right.DisplayName);
        Assert.Equal(2, result.Right.Version);
        Assert.Equal(["reader"], result.Right.Roles);
        Assert.Empty(profile.DomainEvents);
        await publisher.Received(1).EnqueueAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingProfileReturnsNotFoundWithoutPublishing()
    {
        var repository = Substitute.For<IProfileRepository>();
        var publisher = Substitute.For<IIntegrationEventPublisher>();
        var service = new ProfileService(repository, new Unit(), publisher, TimeProvider.System);

        var result = await service.ChangeAsync(Guid.NewGuid(), new("changed", [], 1), new("https://issuer.example", "admin"));

        Assert.True(result.IsLeft);
        Assert.Equal("not_found", result.Left.Code);
        await publisher.DidNotReceive().EnqueueAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidProfileReturnsValidationWithoutAddingOrPublishing()
    {
        var repository = Substitute.For<IProfileRepository>();
        var publisher = Substitute.For<IIntegrationEventPublisher>();
        var service = new ProfileService(repository, new Unit(), publisher, TimeProvider.System);

        var result = await service.CreateAsync(new("", "subject", "name", []), new("https://issuer.example", "admin"));

        Assert.True(result.IsLeft);
        Assert.Equal("validation", result.Left.Code);
        repository.DidNotReceive().Add(Arg.Any<UserProfile>());
        await publisher.DidNotReceive().EnqueueAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderCreationPreservesSuccessAndValidationResults()
    {
        var repository = Substitute.For<IOrderRepository>();
        var service = new OrderService(repository, new Unit(), TimeProvider.System);
        var actor = new AuditActor("https://issuer.example", "subject");

        var created = await service.CreateAsync(new(" SKU-1 ", 2), actor);
        var invalid = await service.CreateAsync(new("SKU-1", 0), actor);

        Assert.True(created.IsRight);
        Assert.Equal("SKU-1", created.Right.Sku);
        Assert.Equal(2, created.Right.Quantity);
        Assert.True(invalid.IsLeft);
        Assert.Equal("validation", invalid.Left.Code);
        repository.Received(1).Add(Arg.Any<Order>());
    }

    [Fact]
    public async Task AuditBusinessKeyPreventsAnotherEffectForADifferentMessageId()
    {
        var repository = Substitute.For<IProfileAuditRepository>();
        repository.HasEffectAsync(Arg.Any<Guid>(), 1, "audit", Arg.Any<CancellationToken>()).Returns(true);
        var handler = new ProfileAuditHandler(repository);
        var message = new UserProfileChangedV1(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), 1,
            "profile.created", "https://issuer.example", "admin");
        await handler.HandleAsync(message, new(message.Id, "audit"), default);
        repository.DidNotReceive().Add(Arg.Any<ProfileAuditEntry>());
    }

    private sealed class Unit(bool failCommit = false) : IUnitOfWork
    {
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            var result = await operation(cancellationToken);
            if (failCommit) throw new IOException("commit failed");
            return result;
        }
    }
}
