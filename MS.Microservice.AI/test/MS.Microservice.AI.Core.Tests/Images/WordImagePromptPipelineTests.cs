using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MS.Microservice.AI.Core.Images;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Tests.Images;

public sealed class WordImagePromptPipelineTests
{
    // ═══════════════════════════════════════════════════════════════
    // Parse
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_ShouldDetectAlphabetCard()
    {
        var result = WordImagePromptPipeline.Parse("A");

        result.ContentType.Should().Be(WordImageCardType.Alphabet);
        result.TargetText.Should().Be("A");
    }

    [Fact]
    public void Parse_ShouldDetectWordCard()
    {
        var result = WordImagePromptPipeline.Parse("apple");

        result.ContentType.Should().Be(WordImageCardType.Word);
        result.TargetText.Should().Be("apple");
    }

    [Fact]
    public void Parse_ShouldDetectPhraseCard()
    {
        var result = WordImagePromptPipeline.Parse("Thank you");

        result.ContentType.Should().Be(WordImageCardType.Phrase);
    }

    [Fact]
    public void Parse_ShouldDetectSentenceCard_ByWordCount()
    {
        var result = WordImagePromptPipeline.Parse("Be careful when you cross the street");

        result.ContentType.Should().Be(WordImageCardType.Sentence);
    }

    [Fact]
    public void Parse_ShouldDetectSentenceCard_ByPunctuation()
    {
        var result = WordImagePromptPipeline.Parse("Don't run!");

        result.ContentType.Should().Be(WordImageCardType.Sentence);
    }

    [Fact]
    public void Parse_ShouldExtractMeaningHint()
    {
        var result = WordImagePromptPipeline.Parse("apple (fruit)");

        result.TargetText.Should().Be("apple");
        result.MeaningHint.Should().Be("fruit");
    }

    [Fact]
    public void Parse_ShouldHandleNestedParentheses()
    {
        var result = WordImagePromptPipeline.Parse("run (move fast)");

        result.MeaningHint.Should().Be("move fast");
        result.TargetText.Should().Be("run");
    }

    [Fact]
    public void Parse_ShouldHandleDeeplyNestedParentheses()
    {
        var result = WordImagePromptPipeline.Parse("apple (fruit (red variety))");

        result.MeaningHint.Should().Be("fruit (red variety)");
        result.TargetText.Should().Be("apple");
    }

    [Fact]
    public void Parse_ShouldHandleNestedParentheses_WithMultipleClosing()
    {
        var result = WordImagePromptPipeline.Parse("box (container (wooden (small)))");

        result.MeaningHint.Should().Be("container (wooden (small))");
        result.TargetText.Should().Be("box");
    }

    [Fact]
    public void Parse_ShouldNotExtractHint_WhenOnlyParentheses()
    {
        var result = WordImagePromptPipeline.Parse("(hello)");

        result.TargetText.Should().Be("(hello)");
        result.MeaningHint.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // InferCardType
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("A", WordImageCardType.Alphabet)]
    [InlineData("z", WordImageCardType.Alphabet)]
    [InlineData("apple", WordImageCardType.Word)]
    [InlineData("it's", WordImageCardType.Word)]
    [InlineData("Thank you", WordImageCardType.Phrase)]
    [InlineData("good morning", WordImageCardType.Phrase)]
    [InlineData("How are you?", WordImageCardType.Sentence)]
    [InlineData("Don't run!", WordImageCardType.Sentence)]
    [InlineData("123", WordImageCardType.Abstract)]
    public void InferCardType_ShouldReturnCorrectType(string text, string expectedType)
    {
        var result = WordImagePromptPipeline.InferCardType(text);

        result.Should().Be(expectedType);
    }

    // ═══════════════════════════════════════════════════════════════
    // GeneratePromptsAsync - fallback when LLM fails
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GeneratePromptsAsync_ShouldReturnFallback_WhenPlanClientFails()
    {
        // Use a plan client that throws to simulate LLM failure
        var planClient = new ThrowingPlanGeneratorClient();
        var pipeline = new WordImagePromptPipeline(planClient, NullLogger<WordImagePromptPipeline>.Instance);

        var (rich, safe) = await pipeline.GeneratePromptsAsync("apple");

        // Should still produce prompts (fallback behavior)
        rich.Should().NotBeNull();
        safe.Should().NotBeNull();
    }

    [Fact]
    public async Task GeneratePromptsAsync_ShouldReturnPrompts_ForSimpleWord()
    {
        var planClient = new StaticPlanGeneratorClient(new WordImageVisualPlan
        {
            VisualMeaning = "A red apple on a table",
            MainSubject = "a red apple",
            SceneSetting = "a kitchen table"
        });
        var pipeline = new WordImagePromptPipeline(planClient, NullLogger<WordImagePromptPipeline>.Instance);

        var (rich, safe) = await pipeline.GeneratePromptsAsync("apple (fruit)");

        rich.Should().NotBeNull();
        rich.Should().Contain("apple");
        safe.Should().NotBeNull();
        safe.Should().NotContain("barefoot");
    }

    [Fact]
    public async Task GenerateSafePromptAsync_ShouldReturnSafePrompt()
    {
        var planClient = new StaticPlanGeneratorClient(new WordImageVisualPlan
        {
            VisualMeaning = "A child running",
            MainSubject = "a child running"
        });
        var pipeline = new WordImagePromptPipeline(planClient, NullLogger<WordImagePromptPipeline>.Instance);

        var safe = await pipeline.GenerateSafePromptAsync("Run!");

        safe.Should().NotBeNull();
    }

    [Fact]
    public async Task GenerateRichPromptAsync_ShouldReturnRichPrompt()
    {
        var planClient = new StaticPlanGeneratorClient(new WordImageVisualPlan
        {
            VisualMeaning = "A child running",
            MainSubject = "a child running"
        });
        var pipeline = new WordImagePromptPipeline(planClient, NullLogger<WordImagePromptPipeline>.Instance);

        var rich = await pipeline.GenerateRichPromptAsync("Run!");

        rich.Should().NotBeNull();
        rich.Should().Contain("running");
    }

    // ═══════════════════════════════════════════════════════════════
    // Test doubles
    // ═══════════════════════════════════════════════════════════════

    private sealed class StaticPlanGeneratorClient : IPlanGeneratorClient
    {
        private readonly WordImageVisualPlan? visualPlan;

        public StaticPlanGeneratorClient(WordImageVisualPlan? visualPlan = null)
        {
            this.visualPlan = visualPlan;
        }

        public Task<T?> SendAsJsonAsync<T>(string systemPrompt, string userMessage, string? model, CancellationToken ct = default)
            where T : class
        {
            if (typeof(T) == typeof(WordImageVisualPlan) && visualPlan is not null)
                return Task.FromResult(visualPlan as T);
            return Task.FromResult<T?>(null);
        }

        public Task<WordImagePromptPlan?> GenerateAlphabetPlanAsync(WordImageInput input, CancellationToken ct = default)
        {
            return Task.FromResult<WordImagePromptPlan?>(new WordImagePromptPlan
            {
                MainSubject = input.TargetText,
                AllowVisibleText = true,
                OverlayText = input.TargetText
            });
        }

        public Task<WordImageVisualPlan?> GenerateVisualPlanAsync(WordImageInput input, CancellationToken ct = default)
        {
            return Task.FromResult(visualPlan);
        }
    }

    private sealed class ThrowingPlanGeneratorClient : IPlanGeneratorClient
    {
        public Task<T?> SendAsJsonAsync<T>(string systemPrompt, string userMessage, string? model, CancellationToken ct = default)
            where T : class
            => throw new InvalidOperationException("Simulated LLM failure");

        public Task<WordImagePromptPlan?> GenerateAlphabetPlanAsync(WordImageInput input, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated LLM failure");

        public Task<WordImageVisualPlan?> GenerateVisualPlanAsync(WordImageInput input, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated LLM failure");
    }
}
