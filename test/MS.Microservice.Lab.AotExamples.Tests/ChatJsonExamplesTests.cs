using Xunit;
using MS.Microservice.Lab.AotExamples.Static.AI;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class ChatJsonExamplesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("0.5")]
    public void GeneratedMetadataPreservesLegacyWireShape(string? temperature)
    {
        var value = new ChatPayload("model", temperature, ["hello", "你好"]);
        var legacy = MS.Microservice.Lab.AotExamples.Legacy.AI.ChatJson.Serialize(value);
        var current = ChatJson.Serialize(value);
        Assert.Equal(legacy, current);
        Assert.Equal(value.Messages, ChatJson.Deserialize(current)!.Messages);
        Assert.Equal(value.Model, ChatJson.Deserialize(current)!.Model);
    }
}


