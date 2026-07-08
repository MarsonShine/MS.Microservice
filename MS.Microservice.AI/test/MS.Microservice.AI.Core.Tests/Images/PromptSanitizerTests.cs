using FluentAssertions;
using MS.Microservice.AI.Core.Images.Helpers;

namespace MS.Microservice.AI.Core.Tests.Images;

public sealed class PromptSanitizerTests
{
    // ═══════════════════════════════════════════════════════════════
    // Clean - basic
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Clean_ShouldReturnNull_WhenInputIsNull()
    {
        PromptSanitizer.Clean(null).Should().BeNull();
    }

    [Fact]
    public void Clean_ShouldReturnNull_WhenInputIsWhitespace()
    {
        PromptSanitizer.Clean("   ").Should().BeNull();
    }

    [Fact]
    public void Clean_ShouldPreservePositiveText()
    {
        var result = PromptSanitizer.Clean("A child playing happily in the park");
        result.Should().NotBeNull();
        result.Should().Contain("child playing happily");
    }

    // ═══════════════════════════════════════════════════════════════
    // Clean - negation stripping
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Clean_ShouldStripNoPhrases()
    {
        var result = PromptSanitizer.Clean("A child with no bare feet playing");
        result.Should().NotBeNull();
        result.Should().NotContain("no bare feet");
        result.Should().Contain("child");
    }

    [Fact]
    public void Clean_ShouldStripNeverPhrases()
    {
        var result = PromptSanitizer.Clean("Never show violence in the image");
        result.Should().NotBeNull();
        result.Should().NotContain("Never");
        result.Should().NotContain("violence");
    }

    [Fact]
    public void Clean_ShouldStripDontPhrases()
    {
        var result = PromptSanitizer.Clean("Don't include bare feet in the scene");
        // "Don't include bare feet" all gets stripped, leaving only "in the scene"
        result.Should().NotBeNull();
        result.Should().NotContain("Don't");
        result.Should().NotContain("bare feet");
        result.Should().Contain("scene");
    }

    // ═══════════════════════════════════════════════════════════════
    // Clean - sensitive word removal
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("violence")]
    [InlineData("blood")]
    [InlineData("injury")]
    [InlineData("accident")]
    [InlineData("weapon")]
    [InlineData("barefoot")]
    [InlineData("naked")]
    public void Clean_ShouldRemoveSensitiveWord(string word)
    {
        var result = PromptSanitizer.Clean($"A scene with {word} present");
        result.Should().NotBeNull();
        result.Should().NotContain(word);
    }

    // ═══════════════════════════════════════════════════════════════
    // Clean - artifact cleanup
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Clean_ShouldCleanupDoubleSpaces()
    {
        var result = PromptSanitizer.Clean("A   child    playing");
        result.Should().NotBeNull();
        result.Should().NotContain("   ");
    }

    [Fact]
    public void Clean_ShouldCleanupLeadingTrailingPunctuation()
    {
        var result = PromptSanitizer.Clean(",;. A child playing.,;");
        result.Should().Be("A child playing");
    }

    [Fact]
    public void Clean_ShouldReturnNull_WhenEverythingStripped()
    {
        // A phrase of only sensitive words should become null
        var result = PromptSanitizer.Clean("violence blood injury accident");
        result.Should().BeNull();
    }
}
