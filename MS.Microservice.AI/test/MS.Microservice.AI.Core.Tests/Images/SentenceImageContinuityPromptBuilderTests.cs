using FluentAssertions;
using MS.Microservice.AI.Core.Images.Building;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Tests.Images;

public sealed class SentenceImageContinuityPromptBuilderTests
{
    // ═══════════════════════════════════════════════════════════════
    // BuildSceneContext
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildSceneContext_ShouldIncludeGroupType()
    {
        var group = new VisualContextGroup
        {
            GroupId = "G1",
            GroupType = "dialogue",
            RowIds = [1, 2],
            SceneSetting = "classroom doorway"
        };

        var result = SentenceImageContinuityPromptBuilder.BuildSceneContext(group, null);

        result.Should().Contain("dialogue");
        result.Should().Contain("classroom doorway");
    }

    [Fact]
    public void BuildSceneContext_ShouldIncludeSpeaker()
    {
        var group = new VisualContextGroup
        {
            GroupId = "G1",
            RowIds = [1]
        };
        var member = new VisualContextMember
        {
            RowId = 1,
            Speaker = "Tom",
            VisualFocus = "Hello!",
            VisualAction = "Waving hello"
        };

        var result = SentenceImageContinuityPromptBuilder.BuildSceneContext(group, member);

        result.Should().Contain("Tom");
        result.Should().Contain("Hello!");
        result.Should().Contain("Waving hello");
    }

    [Fact]
    public void BuildSceneContext_ShouldIncludeCharacterProfiles()
    {
        var group = new VisualContextGroup
        {
            GroupId = "G1",
            RowIds = [1],
            Characters =
            [
                new CharacterProfile { Name = "Tom", Appearance = "Chinese schoolboy, white shirt" }
            ]
        };

        var result = SentenceImageContinuityPromptBuilder.BuildSceneContext(group, null);

        result.Should().Contain("Tom");
        result.Should().Contain("white shirt");
    }

    [Fact]
    public void BuildSceneContext_ShouldIncludeVariableElements()
    {
        var group = new VisualContextGroup { GroupId = "G1", RowIds = [1] };
        var member = new VisualContextMember
        {
            RowId = 1,
            VariableElements = ["a red apple", "a green book"]
        };

        var result = SentenceImageContinuityPromptBuilder.BuildSceneContext(group, member);

        result.Should().Contain("red apple");
        result.Should().Contain("green book");
    }
}
