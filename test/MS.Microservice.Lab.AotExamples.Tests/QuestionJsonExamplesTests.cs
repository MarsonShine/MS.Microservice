using System.Text.Json;
using System.Text.Json.Serialization;
using MS.Microservice.Lab.AotExamples.Static.AI;
using MS.Microservice.Lab.AotExamples.Legacy.AI;
using Xunit;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class QuestionJsonExamplesTests
{
    [Theory]
    [InlineData(QuestionDifficulty.Basic)]
    [InlineData(QuestionDifficulty.Advanced)]
    public void ExplicitHostMetadataKeepsEnumWireValues(QuestionDifficulty difficulty)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter<QuestionDifficulty>(JsonNamingPolicy.CamelCase, false));
        var context = new QuestionExampleJsonContext(options);
        var current = new RegisteredQuestionJson(context.ExampleQuestion);
        var legacy = new LegacyQuestionContract();
        var value = new ExampleQuestion("What is two plus two?", difficulty);
        Assert.Equal(legacy.Serialize(value), current.Serialize(value));
        Assert.Equal(value, current.Deserialize(current.Serialize(value), typeof(ExampleQuestion)));
        Assert.Throws<NotSupportedException>(() => current.Serialize(new Version(1, 2)));
        Assert.Throws<JsonException>(() => current.Deserialize("""{"stem":"test","difficulty":0}""", typeof(ExampleQuestion)));
    }
}
