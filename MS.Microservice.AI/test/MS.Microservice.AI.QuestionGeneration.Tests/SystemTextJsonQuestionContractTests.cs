using System.Text.Json;
using FluentAssertions;
using MS.Microservice.AI.QuestionGeneration.Serialization;

namespace MS.Microservice.AI.QuestionGeneration.Tests;

public sealed class SystemTextJsonQuestionContractTests
{
    private readonly SystemTextJsonQuestionContract contract = TestData.JsonContract();

    [Fact]
    public void GeneratedMetadataSupportsNestedHostTypesWithoutReflection()
    {
        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
        var value = new NestedAnswer(new("解释", AnswerLevel.Advanced), 2);
        var json = contract.Serialize(value);
        Assert.Contains("\"level\":\"advanced\"", json);
        Assert.Equal(value, contract.Deserialize(json, typeof(NestedAnswer)));
        var schema = contract.GetStrictSchema(typeof(NestedAnswer));
        var nested = schema.GetProperty("properties").GetProperty("details");
        Assert.False(nested.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains("advanced", nested.GetProperty("properties").GetProperty("level").GetProperty("enum")
            .EnumerateArray().Select(item => item.GetString()));
    }

    [Theory]
    [InlineData("{\"details\":{\"explanation\":\"ok\",\"level\":1},\"count\":2}")]
    [InlineData("{\"details\":{\"explanation\":\"ok\",\"level\":\"advanced\"},\"count\":\"2\"}")]
    [InlineData("{\"details\":{\"explanation\":\"ok\",\"level\":\"advanced\",\"extra\":1},\"count\":2}")]
    [InlineData("{\"details\":{\"explanation\":\"ok\",\"level\":\"advanced\"},\"Count\":2}")]
    [InlineData("null")]
    [InlineData("{} {}")]
    [InlineData("{\"count\":2,}")]
    public void StrictMetadataRejectsNumericEnumsUnknownNestedFieldsAndPermissiveJson(string json) =>
        Assert.ThrowsAny<JsonException>(() => contract.Deserialize(json, typeof(NestedAnswer)));

    [Fact]
    public void UnknownTypesNeverFallBackToReflection()
    {
        Assert.Throws<NotSupportedException>(() => contract.Serialize(new Version(1, 2)));
        Assert.Throws<NotSupportedException>(() => contract.SerializeToElement(new Version(1, 2)));
        Assert.Throws<NotSupportedException>(() => contract.GetStrictSchema(typeof(Version)));
        Assert.Throws<NotSupportedException>(() => contract.Deserialize("{}", typeof(Version)));
    }

    [Fact]
    public void RegistrationRejectsPermissiveOrDuplicateMetadata()
    {
        Assert.Throws<ArgumentException>(() => new SystemTextJsonQuestionContract([HostQuestionJsonContext.Default.NestedAnswer]));
        Assert.Throws<ArgumentException>(() => new SystemTextJsonQuestionContract([
            TestData.JsonContext.NestedAnswer, TestData.JsonContext.NestedAnswer]));
    }

    [Fact]
    public void Deserialize_ShouldRoundTripHostCandidate()
    {
        var json = contract.Serialize(TestData.Candidate());

        var result = contract.Deserialize(json, typeof(ShortAnswerCandidate));

        result.Should().BeEquivalentTo(TestData.Candidate());
    }

    [Theory]
    [InlineData("```json\n{}\n```")]
    [InlineData("{} trailing")]
    [InlineData("{/*comment*/}")]
    public void Deserialize_ShouldRejectNonStrictJson(string response)
    {
        Action action = () => contract.Deserialize(response, typeof(ShortAnswerCandidate));

        action.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_ShouldRejectUnknownMembers()
    {
        var json = contract.Serialize(TestData.Candidate());
        json = json[..^1] + ",\"unknown\":true}";

        Action action = () => contract.Deserialize(json, typeof(ShortAnswerCandidate));

        action.Should().Throw<JsonException>();
    }

    [Fact]
    public void GetStrictSchema_ShouldDisallowAdditionalProperties()
    {
        var schema = contract.GetStrictSchema(typeof(ShortAnswerCandidate));

        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        schema.GetProperty("required").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(["blueprintId", "questionType", "stem", "answer"]);
    }
}
