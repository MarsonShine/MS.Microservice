using System.Text.Json;
using System.Text.Json.Serialization;
using MS.Microservice.Messaging;
using LegacyExample = MS.Microservice.Lab.AotExamples.Legacy.Messaging;
using StaticExample = MS.Microservice.Lab.AotExamples.Static.Messaging;
using Xunit;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class MessagingContractExampleTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("中文 & quotes \"", -1)]
    [InlineData("", int.MaxValue)]
    public void ExplicitMetadataPreservesLegacyWireFormat(string? text, int count)
    {
        var original = new ExampleEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, text, count);
        var legacy = new LegacyExample.MessageContractRegistry([LegacyExample.MessageContract.For<ExampleEvent>("example.changed")]);
        var current = new StaticExample.MessageContractRegistry([
            StaticExample.MessageContract.For<ExampleEvent>("example.changed", MessagingExampleJsonContext.Default.ExampleEvent)]);

        var oldEnvelope = legacy.Serialize(original);
        var newEnvelope = current.Serialize(original);
        Assert.Equal(oldEnvelope, newEnvelope);
        Assert.Equal(original, current.Deserialize(oldEnvelope));
        Assert.Equal(original, legacy.Deserialize(newEnvelope));
    }

    [Fact]
    public void MetadataDoesNotBypassContractOrEnvelopeValidation()
    {
        var registry = new StaticExample.MessageContractRegistry([
            StaticExample.MessageContract.For<ExampleEvent>("example.changed", MessagingExampleJsonContext.Default.ExampleEvent)]);
        var envelope = registry.Serialize(new ExampleEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, "value", 1));
        Assert.Throws<MessageContractException>(() => registry.Deserialize(envelope with { ContractVersion = 2 }));
        Assert.Throws<MessageContractException>(() => registry.Deserialize(envelope with { Id = Guid.NewGuid() }));
        Assert.Throws<MessageContractException>(() => registry.Deserialize(envelope with { Payload = "null" }));
    }

    public sealed record ExampleEvent(Guid Id, DateTimeOffset OccurredAtUtc, string? Text, int Count) : IIntegrationEvent;
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(MessagingContractExampleTests.ExampleEvent))]
internal partial class MessagingExampleJsonContext : JsonSerializerContext;
