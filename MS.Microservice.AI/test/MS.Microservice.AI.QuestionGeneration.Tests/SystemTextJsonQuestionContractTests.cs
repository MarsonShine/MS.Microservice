using System.Text.Json;
using FluentAssertions;
using MS.Microservice.AI.QuestionGeneration.Serialization;

namespace MS.Microservice.AI.QuestionGeneration.Tests;

public sealed class SystemTextJsonQuestionContractTests
{
    private readonly SystemTextJsonQuestionContract contract = new();

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
