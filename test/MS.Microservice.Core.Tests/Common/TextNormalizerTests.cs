using FluentAssertions;
using MS.Microservice.Core.Common;
using Xunit;

namespace MS.Microservice.Core.Tests.Common;

public sealed class TextNormalizerTests
{
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
