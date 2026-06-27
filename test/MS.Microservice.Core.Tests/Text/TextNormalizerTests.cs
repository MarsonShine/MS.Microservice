using FluentAssertions;
using MS.Microservice.Core.Common;

namespace MS.Microservice.Core.Tests.Text
{
    public class TextNormalizerTests
    {
        [Fact]
        public void Normalize_Null_ReturnsNull()
        {
            TextNormalizer.Normalize(null).Should().BeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t\n ")]
        public void Normalize_Whitespace_ReturnsSame(string input)
        {
            TextNormalizer.Normalize(input).Should().Be(input);
        }

        [Fact]
        public void Normalize_ChineseComma_ReplacedWithEnglishComma()
        {
            TextNormalizer.Normalize("a，b").Should().Be("a,b");
        }

        [Fact]
        public void Normalize_ChinesePunctuation_ReplacedWithEnglish()
        {
            TextNormalizer.Normalize("测试，测试。测试（测试）测试：测试；测试？测试！测试\u201C引号\u201D测试\u2018引号\u2019")
                .Should().Be("测试,测试.测试(测试)测试:测试;测试?测试!测试\"引号\"测试'引号'");
        }

        [Fact]
        public void Normalize_FullWidthSpace_ReplacedWithNormalSpace()
        {
            TextNormalizer.Normalize("a\u3000b").Should().Be("a b");
        }

        [Fact]
        public void Normalize_Nbsp_ReplacedWithNormalSpace()
        {
            TextNormalizer.Normalize("a\u00A0b").Should().Be("a b");
        }

        [Fact]
        public void Normalize_MultipleSpaces_CollapsedToOne()
        {
            TextNormalizer.Normalize("a   b\tc\nd").Should().Be("a b c d");
        }

        [Fact]
        public void Normalize_TrimsLeadingAndTrailingWhitespace()
        {
            TextNormalizer.Normalize("  hello world  ").Should().Be("hello world");
        }

        [Fact]
        public void Normalize_ChineseDunHao_ReplacedWithComma()
        {
            TextNormalizer.Normalize("a、b").Should().Be("a,b");
        }

        [Fact]
        public void Normalize_AllChinesePunctuationMarks_AllReplaced()
        {
            var input = "中文逗号，中文顿号、中文句号。英文句号．左括号（右括号）冒号：分号；问号？感叹号！";
            var expected = "中文逗号,中文顿号,中文句号.英文句号.左括号(右括号)冒号:分号;问号?感叹号!";
            TextNormalizer.Normalize(input).Should().Be(expected);
        }

        [Fact]
        public void Normalize_CombinedChineseAndWhitespace_Normalized()
        {
            TextNormalizer.Normalize("  Hello，\u3000World  ")
                .Should().Be("Hello, World");
        }
    }
}
