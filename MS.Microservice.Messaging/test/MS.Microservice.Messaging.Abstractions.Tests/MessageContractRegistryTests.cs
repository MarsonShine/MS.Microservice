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

    public static IEnumerable<object[]> MetadataCases()
    {
        yield return [null!, null!, null!, true];
        yield return ["", "", "", true];
        yield return [new string('a', 200), new string('p', 128), new string('s', 512), true];
        yield return [new string('中', 85), new string('中', 128), new string('中', 512), true];
        yield return [new string('a', 201), null!, null!, false];
        yield return [new string('中', 86), null!, null!, false];
        yield return [null!, new string('p', 129), null!, false];
        yield return [null!, null!, new string('s', 513), false];
    }

    [Fact]
    public void UnpairedSurrogateCannotBeStoredInAnyMetadataField()
    {
        var invalid = new string((char)0xd800, 1);
        var message = NewEvent();
        var serialized = _registry.Serialize(message);
        foreach (var (correlationId, traceParent, traceState) in new[]
                 {
                     (invalid, (string?)null, (string?)null),
                     (null, invalid, null),
                     (null, null, invalid)
                 })
        {
            Assert.False(MessageMetadataLimits.AreValid(correlationId, traceParent, traceState));
            Assert.Throws<MessageContractException>(() => _registry.Serialize(message,
                new(message.Id, "audit", correlationId, traceParent, traceState)));
            Assert.Throws<MessageContractException>(() => _registry.Deserialize(serialized with
            {
                CorrelationId = correlationId, TraceParent = traceParent, TraceState = traceState
            }));
        }
    }

    [Theory]
    [MemberData(nameof(MetadataCases))]
    public void MetadataLimitsApplyToBothContractDirections(string? correlationId, string? traceParent,
        string? traceState, bool valid)
    {
        var message = NewEvent();
        var context = new MessageContext(message.Id, "audit", correlationId, traceParent, traceState);
        var serialized = _registry.Serialize(message);
        var incoming = serialized with
        {
            CorrelationId = correlationId, TraceParent = traceParent, TraceState = traceState
        };
        Assert.Equal(valid, MessageMetadataLimits.AreValid(correlationId, traceParent, traceState));
        if (valid)
        {
            Assert.Equal(incoming, _registry.Serialize(message, context));
            Assert.Equal(message, _registry.Deserialize(incoming));
        }
        else
        {
            Assert.Throws<MessageContractException>(() => _registry.Serialize(message, context));
            Assert.Throws<MessageContractException>(() => _registry.Deserialize(incoming));
        }
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

    [Theory]
    [InlineData(83, 1, true)]
    [InlineData(84, 1, false)]
    [InlineData(80, int.MaxValue, true)]
    [InlineData(81, int.MaxValue, false)]
    public void ContractRegistrationChecksFullRoutingKeyBytes(int characters, int version, bool valid)
    {
        var name = new string('中', characters);
        var contract = MessageContract.For<Changed>(name, version);
        if (valid)
        {
            var registry = new MessageContractRegistry([contract]);
            Assert.Equal(name, registry.Get(typeof(Changed)).Name);
        }
        else Assert.Throws<ArgumentException>(() => new MessageContractRegistry([contract]));
    }

    [Fact]
    public void AsciiColumnBoundaryAndDottedNamesRemainValid()
    {
        var name = "segment." + new string('a', 192);
        var registry = new MessageContractRegistry([MessageContract.For<Changed>(name, int.MaxValue)]);
        Assert.Equal(name, registry.Get(typeof(Changed)).Name);
        Assert.Throws<ArgumentException>(() => new MessageContractRegistry([
            MessageContract.For<Changed>(name + "a", int.MaxValue)]));
    }

    [Fact]
    public void ContractNameMustBeLosslesslyEncodableAsUtf8()
    {
        var invalid = "event." + new string((char)0xd800, 1);
        Assert.Throws<ArgumentException>(() => new MessageContractRegistry([MessageContract.For<Changed>(invalid)]));
    }

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
