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

    // ═══════════════════════════════════════════════════════════════
    // BuildImageEditContext
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildImageEditContext_ShouldContainFreezeInstruction()
    {
        var group = new VisualContextGroup { GroupId = "G1", RowIds = [1, 2] };

        var result = SentenceImageContinuityPromptBuilder.BuildImageEditContext(group, null);

        result.Should().Contain("IMAGE EDIT DELTA");
        result.Should().Contain("reference image");
    }

    [Fact]
    public void BuildImageEditContext_ShouldPreserveStableCharacters()
    {
        var group = new VisualContextGroup
        {
            GroupId = "G1",
            RowIds = [1, 2],
            Characters =
            [
                new CharacterProfile { Name = "Amy", Appearance = "Chinese schoolgirl, ponytail" }
            ]
        };
        var member = new VisualContextMember
        {
            RowId = 2,
            VisualFocus = "A book on the desk",
            VisualAction = "Pointing at the book"
        };

        var result = SentenceImageContinuityPromptBuilder.BuildImageEditContext(group, member);

        result.Should().Contain("Amy");
        result.Should().Contain("book");
        result.ToLowerInvariant().Should().Contain("do not redesign");
    }

    // ═══════════════════════════════════════════════════════════════
    // BuildImageEditContext — with SentenceImageReferenceContext overload
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildImageEditContext_WithReference_ShouldIncludeReferenceRowInfo()
    {
        var group = new VisualContextGroup
        {
            GroupId = "G1",
            RowIds = [1, 2],
            SceneSetting = "classroom desk"
        };
        var reference = new SentenceImageReferenceContext(
            "https://example.com/img1.png",
            "This is a box.",
            "a box on the desk",
            "Pointing at the box",
            ["a box", "a desk"]);
        var member = new VisualContextMember
        {
            RowId = 2,
            VisualFocus = "A book on the desk",
            VisualAction = "Pointing at the book",
            VariableElements = ["a book", "a desk"]
        };

        var result = SentenceImageContinuityPromptBuilder.BuildImageEditContext(
            group, reference, "This is a book.", member);

        result.Should().Contain("IMAGE EDIT DELTA");
        result.Should().Contain("Reference image currently illustrates");
        result.Should().Contain("This is a box.");
        result.Should().Contain("Reference row visual focus: a box on the desk");
        result.Should().Contain("Current target sentence");
        result.Should().Contain("This is a book.");
        result.Should().Contain("Current target visual focus: A book on the desk");
    }

    [Fact]
    public void BuildImageEditContext_WithReference_ShouldIncludeReplaceInstruction()
    {
        var group = new VisualContextGroup
        {
            GroupId = "G1",
            RowIds = [1, 2],
            Characters =
            [
                new CharacterProfile { Name = "Tom", Appearance = "schoolboy" }
            ]
        };
        var reference = new SentenceImageReferenceContext(
            "https://example.com/img1.png",
            "A box.",
            "a box",
            "pointing at box",
            ["a box"]);
        var member = new VisualContextMember
        {
            RowId = 2,
            VisualFocus = "A book",
            VisualAction = "pointing at book",
            VariableElements = ["a book"]
        };

        var result = SentenceImageContinuityPromptBuilder.BuildImageEditContext(
            group, reference, "A book.", member);

        result.Should().Contain("Replace or revise only the previous row-specific elements");
        result.Should().Contain("a box");
        result.Should().Contain("a book");
    }

    [Fact]
    public void BuildImageEditContext_WithoutReference_ShouldStillContainDeltaAndTarget()
    {
        var group = new VisualContextGroup { GroupId = "G1", RowIds = [1] };
        var member = new VisualContextMember
        {
            RowId = 1,
            VisualFocus = "A cat",
            VisualAction = "petting the cat"
        };

        var result = SentenceImageContinuityPromptBuilder.BuildImageEditContext(
            group, member);

        result.Should().Contain("IMAGE EDIT DELTA");
        result.Should().Contain("Current target visual focus: A cat");
        // Should not contain reference row info
        result.Should().NotContain("Reference row");
    }

    [Fact]
    public void BuildImageEditContext_WithReference_ShouldPreserveCameraAndLighting()
    {
        var group = new VisualContextGroup { GroupId = "G1", RowIds = [1, 2] };
        var reference = new SentenceImageReferenceContext(
            "url", "text", "", "", []);

        var result = SentenceImageContinuityPromptBuilder.BuildImageEditContext(
            group, reference, "New text.", null);

        result.Should().Contain("camera angle");
        result.Should().Contain("lighting");
        result.Should().Contain("color palette");
    }

    [Fact]
    public void BuildImageEditContext_WithReference_ShouldWarnAboutTextElements()
    {
        var group = new VisualContextGroup { GroupId = "G1", RowIds = [1, 2] };
        var reference = new SentenceImageReferenceContext(
            "url", "text", "", "", []);

        var result = SentenceImageContinuityPromptBuilder.BuildImageEditContext(
            group, reference, "No text please.", null);

        result.Should().Contain("Do not add readable text, signs, labels, boards, captions, or speech bubbles");
    }
}
