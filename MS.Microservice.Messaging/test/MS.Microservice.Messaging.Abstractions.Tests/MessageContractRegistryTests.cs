using MS.Microservice.Messaging;
using Xunit;

namespace MS.Microservice.Messaging.Abstractions.Tests;

public sealed class MessageContractRegistryTests
{
    private readonly MessageContractRegistry _registry = new([MessageContract.For<Changed>("profile.changed")]);

    [Theory]
    [InlineData("中文", 7)]
    [InlineData("", 0)]
    [InlineData(null, -1)]
    [InlineData("quotes \" and &", int.MaxValue)]
    public void PersistenceRoundTrip_PreservesIdentityAndValues(string? name, int count)
    {
        var original = new Changed(Guid.NewGuid(), DateTimeOffset.UtcNow, name, count, State.Active, new("nested"));
        var encoded = _registry.Serialize(original, new(original.Id, "audit", "correlation"));
        Assert.Contains("\"name\":", encoded.Payload);
        Assert.DoesNotContain(typeof(Changed).Assembly.GetName().Name!, encoded.ContractName);
        Assert.Equal(original, _registry.Deserialize(encoded));
        Assert.Equal("correlation", encoded.CorrelationId);
    }

    [Fact]
    public void RejectsUnknownVersion_WithoutDynamicTypeLoading()
    {
        var message = _registry.Serialize(NewEvent());
        Assert.Throws<MessageContractException>(() => _registry.Deserialize(message with { ContractVersion = 2 }));
        Assert.Throws<MessageContractException>(() => _registry.Deserialize(message with { ContractName = typeof(string).AssemblyQualifiedName! }));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{}")]
    public void RejectsInvalidOrUnidentifiedPayload(string payload)
    {
        var message = _registry.Serialize(NewEvent()) with { Payload = payload };
        Assert.Throws<MessageContractException>(() => _registry.Deserialize(message));
    }

    [Fact]
    public void RejectsEnvelopeIdentityMismatch()
    {
        var message = _registry.Serialize(NewEvent());
        Assert.Throws<MessageContractException>(() => _registry.Deserialize(message with { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void RejectsDuplicateRegistration()
    {
        var contract = MessageContract.For<Changed>("profile.changed");
        Assert.Throws<ArgumentException>(() => new MessageContractRegistry([contract, contract]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidVersion(int version)
        => Assert.Throws<ArgumentException>(() => new MessageContractRegistry([MessageContract.For<Changed>("profile.changed", version)]));

    [Fact]
    public void RejectsEmptyIdOrNonUtcTime()
    {
        Assert.Throws<MessageContractException>(() => _registry.Serialize(NewEvent() with { Id = Guid.Empty }));
        Assert.Throws<MessageContractException>(() => _registry.Serialize(NewEvent() with { OccurredAtUtc = DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(8)) }));
    }

    private static Changed NewEvent() => new(Guid.NewGuid(), DateTimeOffset.UtcNow, "value", 1, State.Active, null);
    public enum State { Inactive, Active }
    public sealed record Details(string Value);
    public sealed record Changed(Guid Id, DateTimeOffset OccurredAtUtc, string? Name, int Count, State State,
        Details? Details) : IIntegrationEvent;
}
