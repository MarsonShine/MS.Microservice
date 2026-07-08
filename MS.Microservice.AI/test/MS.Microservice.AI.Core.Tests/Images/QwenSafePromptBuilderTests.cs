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
}
