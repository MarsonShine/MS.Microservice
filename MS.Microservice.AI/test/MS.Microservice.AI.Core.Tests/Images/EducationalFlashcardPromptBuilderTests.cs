using FluentAssertions;
using MS.Microservice.AI.Core.Images.Building;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Tests.Images;

public sealed class EducationalFlashcardPromptBuilderTests
{
    // ═══════════════════════════════════════════════════════════════
    // Alphabet card
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_ShouldProduceAlphabetCardPrompt()
    {
        var input = new WordImageInput("A", "A", "letter A", WordImageCardType.Alphabet);
        var plan = new WordImagePromptPlan
        {
            MainSubject = "ant",
            AllowVisibleText = true,
            OverlayText = "A"
        };

        var prompt = EducationalFlashcardPromptBuilder.Build(input, plan);

        prompt.Should().Contain("letter \"A\"");
        prompt.Should().Contain("ant");
        prompt.Should().Contain("pastel border");
        // Rich prompt contains "example sentences" in text-prevention boilerplate for all card types.
        // Verify it is the alphabet path by checking for alphabet-specific content.
        prompt.Should().Contain("exact target letter");
    }

    [Fact]
    public void Build_ShouldUseTargetText_WhenPlanIsNull()
    {
        var input = new WordImageInput("apple", "apple", null, WordImageCardType.Word);

        var prompt = EducationalFlashcardPromptBuilder.Build(input, null);

        // When plan is null, the builder falls back to the input's meaning hint or target text
        prompt.Should().Contain("apple");
        prompt.Should().NotBeNullOrWhiteSpace();
    }

    // ═══════════════════════════════════════════════════════════════
    // Word card
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_ShouldProduceWordCardPrompt()
    {
        var input = new WordImageInput("apple (fruit)", "apple", "fruit", WordImageCardType.Word);
        var plan = new WordImagePromptPlan
        {
            MainSubject = "a red apple",
            SceneSetting = "a kitchen table"
        };

        var prompt = EducationalFlashcardPromptBuilder.Build(input, plan);

        prompt.Should().Contain("red apple");
        prompt.Should().Contain("kitchen table");
        prompt.Should().Contain("teaching card");
    }

    // ═══════════════════════════════════════════════════════════════
    // Sentence card
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_ShouldProduceSentenceCardPrompt()
    {
        var input = new WordImageInput("Don't run in the classroom.", "Don't run in the classroom.", null, WordImageCardType.Sentence);
        var plan = new WordImagePromptPlan
        {
            MainSubject = "A child running between desks while a teacher gestures to stop",
            PrimaryActor = "a child",
            SecondaryActor = "a teacher",
            RequiredAction = "the child running between desks",
            ProhibitedAction = "running",
            WarningCue = "teacher raises open palm in stopping gesture",
            SceneSetting = "a bright classroom",
            SettingCues = ["desks", "chairs", "windows", "blank board"]
        };

        var prompt = EducationalFlashcardPromptBuilder.Build(input, plan);

        prompt.Should().Contain("classroom");
        prompt.Should().Contain("running");
        prompt.Should().Contain("teacher");
        prompt.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Build_ShouldIncludeSettingCues_WhenProvided()
    {
        var input = new WordImageInput("Be careful! Play safely.", "Be careful! Play safely.", null, WordImageCardType.Sentence);
        var plan = new WordImagePromptPlan
        {
            MainSubject = "A child carefully sliding down a slide",
            SceneSetting = "a playground",
            SettingCues = ["a slide"]
        };

        var prompt = EducationalFlashcardPromptBuilder.Build(input, plan);

        prompt.Should().Contain("a slide");
        // The rich prompt includes a text-prevention section that mentions "blackboards" generically.
        // The key check is that the scene setting and cues are playground-themed, not classroom-themed.
        prompt.Should().NotContain("classroom");
        prompt.Should().Contain("playground");
    }

    [Fact]
    public void Build_ShouldIncludeMustShow_WhenProvided()
    {
        var input = new WordImageInput("Be careful!", "Be careful!", null, WordImageCardType.Sentence);
        var plan = new WordImagePromptPlan
        {
            MainSubject = "A child near a step",
            MustShow = ["a visible step or obstacle", "an adult watching nearby"]
        };

        var prompt = EducationalFlashcardPromptBuilder.Build(input, plan);

        prompt.Should().Contain("must visibly include");
        prompt.Should().Contain("step or obstacle");
    }

    // ═══════════════════════════════════════════════════════════════
    // Common constraints in all prompts
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_ShouldAlwaysContainZeroTextConstraint()
    {
        var input = new WordImageInput("Hello", "Hello", null, WordImageCardType.Word);

        var prompt = EducationalFlashcardPromptBuilder.Build(input, null);

        prompt.Should().Contain("zero visible text");
    }

    [Fact]
    public void Build_ShouldAlwaysContainFootwearConstraint()
    {
        var input = new WordImageInput("Run", "Run", null, WordImageCardType.Word);

        var prompt = EducationalFlashcardPromptBuilder.Build(input, null);

        prompt.Should().Contain("footwear");
    }

    [Fact]
    public void Build_ShouldAlwaysContainNoPoliticalContent()
    {
        var input = new WordImageInput("Hello", "Hello", null, WordImageCardType.Sentence);

        var prompt = EducationalFlashcardPromptBuilder.Build(input, null);

        prompt.Should().Contain("flags");
        prompt.Should().Contain("political");
    }

    [Fact]
    public void Build_ShouldIncludeNegativeElements_WhenProvided()
    {
        var input = new WordImageInput("Play", "Play", null, WordImageCardType.Word);
        var plan = new WordImagePromptPlan
        {
            MainSubject = "children playing",
            NegativeElements = ["violence", "crowds", "night scene"]
        };

        var prompt = EducationalFlashcardPromptBuilder.Build(input, plan);

        prompt.Should().Contain("violence");
        prompt.Should().Contain("crowds");
    }

    // ═══════════════════════════════════════════════════════════════
    // Phrase card
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_ShouldProducePhraseCardPrompt()
    {
        var input = new WordImageInput("Thank you", "Thank you", null, WordImageCardType.Phrase);
        var plan = new WordImagePromptPlan
        {
            MainSubject = "a child bowing slightly with hands together",
            ActionOrGesture = "bowing slightly with hands together"
        };

        var prompt = EducationalFlashcardPromptBuilder.Build(input, plan);

        prompt.Should().Contain("bowing");
        // The rich prompt always contains "example sentences" as part of the
        // text-prevention boilerplate. Verify the card-type section is about a phrase.
        prompt.Should().Contain("target expression");
    }

    // ═══════════════════════════════════════════════════════════════
    // Abstract card
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Build_ShouldProduceAbstractCardPrompt()
    {
        var input = new WordImageInput("beautiful", "beautiful", null, WordImageCardType.Abstract);
        var plan = new WordImagePromptPlan
        {
            MainSubject = "a beautiful flower"
        };

        var prompt = EducationalFlashcardPromptBuilder.Build(input, plan);

        prompt.Should().Contain("visual metaphor");
        prompt.Should().Contain("beautiful flower");
    }
}
