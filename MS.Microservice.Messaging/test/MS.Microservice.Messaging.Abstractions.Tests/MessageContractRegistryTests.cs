using MS.Microservice.Messaging;
using Xunit;

namespace MS.Microservice.Messaging.Abstractions.Tests;

public sealed class MessageContractRegistryTests
{
    private readonly MessageContractRegistry _registry = new([MessageContract.For<Changed>("profile.changed", TestMessageJsonContext.Default.Changed)]);

    [Fact]
    public void GeneratedMetadataWorksWithDefaultReflectionDisabled()
    {
        Assert.False(System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault);
        var original = NewEvent();
        Assert.Equal(original, _registry.Deserialize(_registry.Serialize(original)));
    }

    [Fact]
    public void RejectsMissingMetadataAndUnregisteredRuntimeType()
    {
        Assert.Throws<ArgumentNullException>(() => MessageContract.For<Changed>("profile.changed", null!));
        Assert.Throws<MessageContractException>(() => _registry.Serialize(new Unregistered(Guid.NewGuid(), DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void RegisteredMetadataDeterminesWireNamesAndNestedEnumRepresentation()
    {
        var original = NewEvent() with { Details = new("nested") };
        var encoded = _registry.Serialize(original);
        using var document = System.Text.Json.JsonDocument.Parse(encoded.Payload);
        Assert.Equal(1, document.RootElement.GetProperty("state").GetInt32());
        Assert.Equal("nested", document.RootElement.GetProperty("details").GetProperty("value").GetString());
        Assert.Equal(original, _registry.Deserialize(encoded with { Payload = encoded.Payload.Replace("\"name\"", "\"NAME\"") }));
    }

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
        Assert.Throws<MessageContractException>(() => _registry.Deserialize(message with { OccurredAtUtc = message.OccurredAtUtc.AddTicks(1) }));
    }

    [Fact]
    public void RejectsDuplicateRegistration()
    {
        var contract = MessageContract.For<Changed>("profile.changed", TestMessageJsonContext.Default.Changed);
        Assert.Throws<ArgumentException>(() => new MessageContractRegistry([contract, contract]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidVersion(int version)
        => Assert.Throws<ArgumentException>(() => new MessageContractRegistry([MessageContract.For<Changed>("profile.changed", TestMessageJsonContext.Default.Changed, version)]));

    [Fact]
    public void RejectsEmptyIdOrNonUtcTime()
    {
        Assert.Throws<MessageContractException>(() => _registry.Serialize(NewEvent() with { Id = Guid.Empty }));
        Assert.Throws<MessageContractException>(() => _registry.Serialize(NewEvent() with { OccurredAtUtc = DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(8)) }));
    }

    private static Changed NewEvent() => new(Guid.NewGuid(), DateTimeOffset.UtcNow, "value", 1, State.Active, null);
    public enum State { Inactive, Active }
    public sealed record Details(string Value);
    private sealed record Unregistered(Guid Id, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
    public sealed record Changed(Guid Id, DateTimeOffset OccurredAtUtc, string? Name, int Count, State State,
        Details? Details) : IIntegrationEvent;
}
