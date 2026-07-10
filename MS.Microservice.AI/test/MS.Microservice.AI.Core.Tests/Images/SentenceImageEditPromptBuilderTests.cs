using FluentAssertions;
using MS.Microservice.AI.Core.Images.Building;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Tests.Images;

public sealed class SentenceImageEditPromptBuilderTests
{
    // ═══════════════════════════════════════════════════════════════
    // CanUseReferenceEdit
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CanUseReferenceEdit_WhenConfidenceBelowThreshold_ShouldReturnFalse()
    {
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.5,
            Operations = [new SentenceImageEditOperation { Operation = "replace", From = "box", To = "apple" }]
        };

        SentenceImageEditPromptBuilder.CanUseReferenceEdit(delta).Should().BeFalse();
    }

    [Fact]
    public void CanUseReferenceEdit_WhenMultipleConcreteOperations_ShouldReturnFalse()
    {
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.9,
            Operations =
            [
                new SentenceImageEditOperation { Operation = "replace", From = "box", To = "apple" },
                new SentenceImageEditOperation { Operation = "add", To = "banana" }
            ]
        };

        SentenceImageEditPromptBuilder.CanUseReferenceEdit(delta).Should().BeFalse();
    }

    [Fact]
    public void CanUseReferenceEdit_WhenNoConcreteOperations_ShouldReturnFalse()
    {
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.9
        };

        SentenceImageEditPromptBuilder.CanUseReferenceEdit(delta).Should().BeFalse();
    }

    [Fact]
    public void CanUseReferenceEdit_WhenNullDelta_ShouldReturnFalse()
    {
        SentenceImageEditPromptBuilder.CanUseReferenceEdit(null).Should().BeFalse();
    }

    [Fact]
    public void CanUseReferenceEdit_WhenSingleConcreteReplace_ShouldReturnTrue()
    {
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "replace", From = "box", To = "apple" }]
        };

        SentenceImageEditPromptBuilder.CanUseReferenceEdit(delta).Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    // BuildPrompt - replace
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildPrompt_Replace_BoxToApple_ShouldOutputOnlyEdit()
    {
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "replace", From = "a box", To = "an apple" }]
        };

        var prompt = SentenceImageEditPromptBuilder.BuildPrompt(delta);
        prompt.Should().Be("Only edit: box -> apple.");
    }

    [Fact]
    public void BuildPrompt_Replace_ShouldRemoveArticles()
    {
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "replace", From = "the cat", To = "a dog" }]
        };

        var prompt = SentenceImageEditPromptBuilder.BuildPrompt(delta);
        prompt.Should().Be("Only edit: cat -> dog.");
    }

    // ═══════════════════════════════════════════════════════════════
    // BuildPrompt - update / change
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildPrompt_Update_ShouldOutputMinimalPrompt()
    {
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "update", Target = "the box color", To = "red" }]
        };

        var prompt = SentenceImageEditPromptBuilder.BuildPrompt(delta);
        prompt.Should().Be("Only edit: box color -> red.");
    }

    [Fact]
    public void BuildPrompt_Change_ShouldNormalizeToUpdate()
    {
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "change", Target = "shirt", To = "blue shirt" }]
        };

        // Note: BuildPrompt uses the raw operation string; normalization is in SentenceEditDeltaAgent
        // We test with "update" since that's what the agent normalizes "change" to
        var prompt = SentenceImageEditPromptBuilder.BuildPrompt(delta);
        prompt.Should().Be("Only edit: shirt -> blue shirt.");
    }

    // ═══════════════════════════════════════════════════════════════
    // BuildPrompt - add
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildPrompt_Add_WithTo_ShouldOutputOnlyAdd()
    {
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "add", To = "a red hat" }]
        };

        var prompt = SentenceImageEditPromptBuilder.BuildPrompt(delta);
        prompt.Should().Be("Only add: red hat.");
    }

    [Fact]
    public void BuildPrompt_Add_WithTargetOnly_ShouldOutputOnlyAdd()
    {
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "add", Target = "sunglasses" }]
        };

        var prompt = SentenceImageEditPromptBuilder.BuildPrompt(delta);
        prompt.Should().Be("Only add: sunglasses.");
    }

    // ═══════════════════════════════════════════════════════════════
    // BuildPrompt - remove
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildPrompt_Remove_WithFrom_ShouldOutputOnlyRemove()
    {
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "remove", From = "the red ball" }]
        };

        var prompt = SentenceImageEditPromptBuilder.BuildPrompt(delta);
        prompt.Should().Be("Only remove: red ball.");
    }

    [Fact]
    public void BuildPrompt_Remove_WithTarget_ShouldOutputOnlyRemove()
    {
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "remove", Target = "backpack" }]
        };

        var prompt = SentenceImageEditPromptBuilder.BuildPrompt(delta);
        prompt.Should().Be("Only remove: backpack.");
    }

    // ═══════════════════════════════════════════════════════════════
    // BuildPrompt - transport normalization
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildPrompt_Transport_GoingByBus_ShouldOutputBus()
    {
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "replace", From = "walking", To = "going by bus" }]
        };

        var prompt = SentenceImageEditPromptBuilder.BuildPrompt(delta);
        prompt.Should().Be("Only edit: walking -> bus.");
    }

    [Fact]
    public void BuildPrompt_Transport_TakingABus_ShouldOutputBus()
    {
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "replace", From = "walking", To = "taking a bus" }]
        };

        var prompt = SentenceImageEditPromptBuilder.BuildPrompt(delta);
        prompt.Should().Be("Only edit: walking -> bus.");
    }

    [Fact]
    public void BuildPrompt_Transport_ByBus_ShouldOutputBus()
    {
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "replace", From = "walking", To = "by bus" }]
        };

        var prompt = SentenceImageEditPromptBuilder.BuildPrompt(delta);
        prompt.Should().Be("Only edit: walking -> bus.");
    }

    [Fact]
    public void BuildPrompt_Transport_WalkingOnFoot_ShouldOutputWalking()
    {
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "replace", From = "bus", To = "walking on foot" }]
        };

        var prompt = SentenceImageEditPromptBuilder.BuildPrompt(delta);
        prompt.Should().Be("Only edit: bus -> walking.");
    }
}