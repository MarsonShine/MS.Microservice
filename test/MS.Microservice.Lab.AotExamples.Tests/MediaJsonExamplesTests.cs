using Xunit;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class MediaJsonExamplesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(1.25)]
    public void NamedSpeechPayloadPreservesAnonymousPayloadWireShape(double? speed)
    {
        var legacy = MS.Microservice.Lab.AotExamples.Legacy.AI.MediaJson.SerializeSpeech("model", "你好", "voice", speed);
        var current = MS.Microservice.Lab.AotExamples.Static.AI.MediaJson.SerializeSpeech("model", "你好", "voice", speed);
        Assert.Equal(legacy, current);
    }
}
