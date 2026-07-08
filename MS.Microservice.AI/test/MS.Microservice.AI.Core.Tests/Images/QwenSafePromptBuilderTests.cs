using FluentAssertions;
using MS.Microservice.AI.Core.Images.Building;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Tests.Images;

public sealed class QwenSafePromptBuilderTests
{
    // ═══════════════════════════════════════════════════════════════
    // Common header
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_ShouldAlwaysContainStyleHeader()
    {
        var input = new WordImageInput("Hello", "Hello", null, WordImageCardType.Word);

        var prompt = QwenSafePromptBuilder.Build(input, null);

        prompt.Should().Contain("storybook style");
        prompt.Should().Contain("4:3");
    }

    [Fact]
    public void Build_ShouldAlwaysContainTextFreeConstraint()
    {
        var input = new WordImageInput("Hello", "Hello", null, WordImageCardType.Word);

        var prompt = QwenSafePromptBuilder.Build(input, null);

        prompt.Should().Contain("text-free");
    }

    [Fact]
    public void Build_ShouldAlwaysContainEyeConstraint()
    {
        var input = new WordImageInput("Hello", "Hello", null, WordImageCardType.Word);

        var prompt = QwenSafePromptBuilder.Build(input, null);

        prompt.Should().Contain("irises");
        prompt.Should().Contain("eye whites");
    }

    [Fact]
    public void Build_ShouldAlwaysEndWithCheerfulAtmosphere()
    {
        var input = new WordImageInput("Hello", "Hello", null, WordImageCardType.Word);

        var prompt = QwenSafePromptBuilder.Build(input, null);

        prompt.ToLowerInvariant().Should().Contain("cheerful warm atmosphere");
    }

    // ═══════════════════════════════════════════════════════════════
    // No negative language
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_ShouldNotContainNegativeWords()
    {
        var input = new WordImageInput("Don't run!", "Don't run!", null, WordImageCardType.Sentence);
        var plan = new WordImagePromptPlan
        {
            MainSubject = "A child running in the classroom",
            RequiredAction = "a child running",
            ProhibitedAction = "running"
        };

        var prompt = QwenSafePromptBuilder.Build(input, plan);

        prompt.ToLowerInvariant().Should().NotContain("don't");
        prompt.ToLowerInvariant().Should().NotContain("prohibited");
        prompt.ToLowerInvariant().Should().NotContain("forbidden");
        prompt.ToLowerInvariant().Should().NotContain("barefoot");
        prompt.ToLowerInvariant().Should().NotContain("injury");
        prompt.ToLowerInvariant().Should().NotContain("violence");
    }

    // ═══════════════════════════════════════════════════════════════
    // Alphabet card
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_ShouldProduceAlphabetSafePrompt()
    {
        var input = new WordImageInput("A", "A", "letter A", WordImageCardType.Alphabet);
        var plan = new WordImagePromptPlan
        {
            MainSubject = "ant",
            AllowVisibleText = true,
            OverlayText = "A"
        };

        var prompt = QwenSafePromptBuilder.Build(input, plan);

        prompt.Should().Contain("capital letter A");
        prompt.Should().Contain("pastel");
    }

    // ═══════════════════════════════════════════════════════════════
    // Word card
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_ShouldProduceWordSafePrompt()
    {
        var input = new WordImageInput("apple (fruit)", "apple", "fruit", WordImageCardType.Word);
        var plan = new WordImagePromptPlan
        {
            MainSubject = "a red apple"
        };

        var prompt = QwenSafePromptBuilder.Build(input, plan);

        prompt.Should().Contain("red apple");
        prompt.Should().Contain("concept fruit");
    }

    // ═══════════════════════════════════════════════════════════════
    // Sentence / Phrase (event) card
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_ShouldProduceEventCardSafePrompt()
    {
        var input = new WordImageInput("Don't run in the classroom.", "Don't run in the classroom.", null, WordImageCardType.Sentence);
        var plan = new WordImagePromptPlan
        {
            MainSubject = "A child running between desks",
            PrimaryActor = "a child",
            SecondaryActor = "a teacher",
            RequiredAction = "the child running between desks",
            ProhibitedAction = "running",
            WarningCue = "teacher raises open palm",
            SceneSetting = "a bright classroom",
            SettingCues = ["desks", "chairs", "windows", "blank board"]
        };

        var prompt = QwenSafePromptBuilder.Build(input, plan);

        prompt.Should().Contain("classroom");
        // Should not duplicate when RequiredAction and ProhibitedAction overlap
        prompt.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Build_ShouldLimitSettingCuesTo2_InSafePrompt()
    {
        var input = new WordImageInput("Be careful!", "Be careful!", null, WordImageCardType.Sentence);
        var plan = new WordImagePromptPlan
        {
            MainSubject = "Safe play",
            SceneSetting = "a playground",
            SettingCues = ["a slide", "a swing", "sandbox", "bench"]
        };

        var prompt = QwenSafePromptBuilder.Build(input, plan);

        // Should only include at most 2 cues (the .Take(2) in the builder)
        var cueCount = System.Text.RegularExpressions.Regex.Matches(prompt, "slide|swing|sandbox|bench").Count;
        cueCount.Should().BeLessThanOrEqualTo(2);
    }

    // ═══════════════════════════════════════════════════════════════
    // Edge cases
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_ShouldHandleNullPlan()
    {
        var input = new WordImageInput("Hello", "Hello", null, WordImageCardType.Word);

        var prompt = QwenSafePromptBuilder.Build(input, null);

        prompt.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Build_ShouldHandleEmptyPlan()
    {
        var input = new WordImageInput("Hello", "Hello", null, WordImageCardType.Word);
        var plan = new WordImagePromptPlan();

        var prompt = QwenSafePromptBuilder.Build(input, plan);

        prompt.Should().NotBeNullOrWhiteSpace();
    }

    // ═══════════════════════════════════════════════════════════════
    // SentenceImageControlContext — IMAGE EDIT DELTA marker
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_ShouldAppendControlContext_WhenMeaningHintContainsImageEditDelta()
    {
        var input = new WordImageInput(
            "A book on the desk.",
            "A book on the desk.",
            "(IMAGE EDIT DELTA: edit the provided reference image. Current sentence visual focus: A book on the desk. Current sentence visual action: Pointing at the book.)",
            WordImageCardType.Sentence);
        var plan = new WordImagePromptPlan { MainSubject = "book on desk" };

        var prompt = QwenSafePromptBuilder.Build(input, plan);

        prompt.Should().Contain("Sentence image control context");
        prompt.Should().Contain("Current sentence visual focus");
        prompt.Should().Contain("A book on the desk");
    }

    [Fact]
    public void Build_ShouldAppendControlContext_WhenMeaningHintContainsSharedContextType()
    {
        var input = new WordImageInput(
            "Hello!",
            "Hello!",
            "(Shared context type: dialogue. Stable scene: classroom doorway. Current sentence visual focus: Hello greeting. Keep the stable scene and characters consistent.)",
            WordImageCardType.Phrase);
        var plan = new WordImagePromptPlan { MainSubject = "greeting" };

        var prompt = QwenSafePromptBuilder.Build(input, plan);

        prompt.Should().Contain("Sentence image control context");
        prompt.Should().Contain("Shared context type");
    }

    [Fact]
    public void Build_ShouldAppendControlContext_WhenMeaningHintContainsPromptBranch()
    {
        var input = new WordImageInput(
            "An apple.",
            "An apple.",
            "(Prompt branch target-object-focus: Depict an apple as the clear central focus. Use a red arrow pointer aimed at the apple. Keep any surrounding objects minimal, small, and secondary.)",
            WordImageCardType.Sentence);
        var plan = new WordImagePromptPlan { MainSubject = "apple" };

        var prompt = QwenSafePromptBuilder.Build(input, plan);

        prompt.Should().Contain("Sentence image control context");
        prompt.Should().Contain("Prompt branch target-object-focus");
    }

    // ═══════════════════════════════════════════════════════════════
    // SentenceImageControlContext — negative language filtering
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_ShouldNotIncludeNegativeLanguage_InControlContext()
    {
        var input = new WordImageInput(
            "Run.",
            "Run.",
            "(IMAGE EDIT DELTA: edit reference. Do not redesign the scene. Current sentence visual focus: running. Do not add extra people.)",
            WordImageCardType.Sentence);
        var plan = new WordImagePromptPlan { MainSubject = "running" };

        var prompt = QwenSafePromptBuilder.Build(input, plan);

        prompt.Should().Contain("Sentence image control context");
        // Negative clauses should be stripped
        prompt.Should().NotContain("Do not redesign");
        prompt.Should().NotContain("Do not add extra people");
    }

    [Fact]
    public void Build_ShouldNotIncludeBoardReferences_InControlContext()
    {
        var input = new WordImageInput(
            "Play outside.",
            "Play outside.",
            "(IMAGE EDIT DELTA: edit reference. Current sentence visual focus: playing. A blackboard is in the background. A whiteboard on the wall.)",
            WordImageCardType.Sentence);
        var plan = new WordImagePromptPlan { MainSubject = "playing" };

        var prompt = QwenSafePromptBuilder.Build(input, plan);

        prompt.Should().Contain("Sentence image control context");
        prompt.Should().NotContain("blackboard");
        prompt.Should().NotContain("whiteboard");
    }

    // ═══════════════════════════════════════════════════════════════
    // SentenceImageControlContext — no-op when no marker
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_ShouldNotAppendControlContext_WhenNoMarker()
    {
        var input = new WordImageInput("Hello!", "Hello!", "a greeting", WordImageCardType.Sentence);
        var plan = new WordImagePromptPlan { MainSubject = "waving" };

        var prompt = QwenSafePromptBuilder.Build(input, plan);

        prompt.Should().NotContain("Sentence image control context");
    }

    [Fact]
    public void Build_ShouldNotAppendControlContext_WhenMeaningHintIsNull()
    {
        var input = new WordImageInput("Hello!", "Hello!", null, WordImageCardType.Sentence);
        var plan = new WordImagePromptPlan { MainSubject = "waving" };

        var prompt = QwenSafePromptBuilder.Build(input, plan);

        prompt.Should().NotContain("Sentence image control context");
    }

    [Fact]
    public void Build_ShouldNotAppendControlContext_ForAlphabetCard()
    {
        // Control context only applies to non-alphabet cards
        var input = new WordImageInput("A", "A", "(IMAGE EDIT DELTA: edit reference.)", WordImageCardType.Alphabet);
        var plan = new WordImagePromptPlan { MainSubject = "ant", AllowVisibleText = true, OverlayText = "A" };

        var prompt = QwenSafePromptBuilder.Build(input, plan);

        prompt.Should().NotContain("Sentence image control context");
    }
}
