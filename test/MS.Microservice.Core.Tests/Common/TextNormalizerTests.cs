using FluentAssertions;
using MS.Microservice.Core.Common;
using Xunit;

namespace MS.Microservice.Core.Tests.Common;

public sealed class TextNormalizerTests
{
    [Fact]
    public void Normalize_PreservesUtf16AndCollapsesUnicodeWhitespace()
    {
        Assert.Equal("📚, A\u200b B.", TextNormalizer.Normalize("\u2028📚，\u0085\u2003A\u200b\u2029B。\u3000"));
        string onlyWhitespace = "\u2003\u0085\u2028";
        Assert.Same(onlyWhitespace, TextNormalizer.Normalize(onlyWhitespace));
    }

    [Fact]
    public void Normalize_ShouldReturnOriginal_WhenInputIsNullOrWhitespace()
    {
        TextNormalizer.Normalize(null).Should().BeNull();
        TextNormalizer.Normalize("   ").Should().Be("   ");
    }

    [Theory]
    [InlineData("你好，世界", "你好,世界")]
    [InlineData("测试（括号）", "测试(括号)")]
    [InlineData("结束。", "结束.")]
    [InlineData("问题？", "问题?")]
    [InlineData("感叹！", "感叹!")]
    [InlineData("“引用”", "\"引用\"")]
    [InlineData("‘单引号’", "'单引号'")]
    public void Normalize_ShouldReplaceChinesePunctuation(string input, string expected)
    {
        TextNormalizer.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_ShouldCollapseWhitespace_AndTrim()
    {
        TextNormalizer.Normalize("  a\u3000\u00A0b   c  ").Should().Be("a b c");
    }
}
