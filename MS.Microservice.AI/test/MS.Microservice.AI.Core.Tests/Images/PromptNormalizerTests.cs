using FluentAssertions;
using MS.Microservice.AI.Core.Images.Helpers;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Tests.Images;

public sealed class PromptNormalizerTests
{
    // ═══════════════════════════════════════════════════════════════
    // NormalizeValue
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NormalizeValue_ShouldReturnFirstNonEmpty()
    {
        var result = PromptNormalizer.NormalizeValue(null, "", "  apple  ", "banana");
        result.Should().Be("apple");
    }

    [Fact]
    public void NormalizeValue_ShouldTrimTrailingDot()
    {
        var result = PromptNormalizer.NormalizeValue("A child running.");
        result.Should().Be("A child running");
    }

    [Fact]
    public void NormalizeValue_ShouldReturnEmpty_WhenAllEmpty()
    {
        var result = PromptNormalizer.NormalizeValue(null, "", "  ");
        result.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // NormalizeSceneSetting
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NormalizeSceneSetting_ShouldReturnEmpty_ForAlphabetCards()
    {
        var result = PromptNormalizer.NormalizeSceneSetting("a classroom", WordImageCardType.Alphabet);
        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeSceneSetting_ShouldReturnScene_ForSentenceCards()
    {
        var result = PromptNormalizer.NormalizeSceneSetting("a bright classroom", WordImageCardType.Sentence);
        result.Should().Be("a bright classroom");
    }

    [Theory]
    [InlineData("a messy room", true)]
    [InlineData("cluttered space", true)]
    [InlineData("a chaotic scene", true)]
    [InlineData("a dark room", true)]
    [InlineData("a crowded hallway", true)]
    public void NormalizeSceneSetting_ShouldFilterBlockedWords(string scene, bool shouldBeEmpty)
    {
        var result = PromptNormalizer.NormalizeSceneSetting(scene, WordImageCardType.Sentence);
        if (shouldBeEmpty)
            result.Should().BeEmpty();
        else
            result.Should().NotBeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // NormalizeOverlayText
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NormalizeOverlayText_ShouldUseOverlay_WhenProvided()
    {
        var result = PromptNormalizer.NormalizeOverlayText("A", "B");
        result.Should().Be("A");
    }

    [Fact]
    public void NormalizeOverlayText_ShouldFallback_WhenOverlayIsEmpty()
    {
        var result = PromptNormalizer.NormalizeOverlayText(null, "Fallback");
        result.Should().Be("Fallback");
    }

    // ═══════════════════════════════════════════════════════════════
    // AddDistinct
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void AddDistinct_ShouldAddItem()
    {
        var list = new List<string> { "item1" };
        PromptNormalizer.AddDistinct(list, "item2");
        list.Should().HaveCount(2);
        list.Should().Contain("item2");
    }

    [Fact]
    public void AddDistinct_ShouldSkipDuplicate_CaseInsensitive()
    {
        var list = new List<string> { "Apple" };
        PromptNormalizer.AddDistinct(list, "apple");
        list.Should().HaveCount(1);
    }

    [Fact]
    public void AddDistinct_ShouldSkipNullOrWhitespace()
    {
        var list = new List<string>();
        PromptNormalizer.AddDistinct(list, null!);
        PromptNormalizer.AddDistinct(list, "  ");
        list.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // NormalizeList
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NormalizeList_ShouldRemoveEmptyAndTrim()
    {
        var list = new List<string> { "  apple.  ", "", "  ", "banana." };
        PromptNormalizer.NormalizeList(list, 10);
        list.Should().Equal("apple", "banana");
    }

    [Fact]
    public void NormalizeList_ShouldTruncateToMaxCount()
    {
        var list = new List<string> { "a", "b", "c", "d", "e" };
        PromptNormalizer.NormalizeList(list, 3);
        list.Should().HaveCount(3);
        list.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void NormalizeList_ShouldRemoveDuplicates()
    {
        var list = new List<string> { "desk", "desk", "chair" };
        PromptNormalizer.NormalizeList(list, 10);
        list.Should().Equal("desk", "chair");
    }

    // ═══════════════════════════════════════════════════════════════
    // ContainsAny
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ContainsAny_ShouldMatchCaseInsensitive()
    {
        var list = new List<string> { "the forbidden action must be visible" };
        PromptNormalizer.ContainsAny(list, "forbidden").Should().BeTrue();
        PromptNormalizer.ContainsAny(list, "FORBIDDEN").Should().BeTrue();
        PromptNormalizer.ContainsAny(list, "absent").Should().BeFalse();
    }

    [Fact]
    public void ContainsAny_ShouldReturnFalse_WhenListIsNull()
    {
        PromptNormalizer.ContainsAny(null!, "test").Should().BeFalse();
    }
}
