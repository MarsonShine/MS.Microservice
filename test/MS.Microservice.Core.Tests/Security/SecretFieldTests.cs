using FluentAssertions;
using MS.Microservice.Core.Security;

namespace MS.Microservice.Core.Tests.Security
{
    public class SecretFieldTests
    {
        [Theory]
        [InlineData("13812345678", "138****5678")]
        [InlineData("1234567890", "123****890")]
        public void Phone_MasksMiddleDigits(string input, string expected)
        {
            SecretField.Phone(input).Should().Be(expected);
        }

        [Fact]
        public void Phone_ShortNumber_ReturnsUnchanged()
        {
            // 长度 <= 9 的不做脱敏
            SecretField.Phone("12345").Should().Be("12345");
        }

        [Fact]
        public void HideEmailDetails_ValidEmail_MasksPrefix()
        {
            var result = SecretField.HideEmailDetails("test@example.com", left: 2);
            result.Should().StartWith("te");
            result.Should().Contain("*");
            result.Should().EndWith("@example.com");
        }

        [Fact]
        public void HideEmailDetails_EmptyString_ReturnsEmpty()
        {
            SecretField.HideEmailDetails("").Should().BeEmpty();
        }

        [Fact]
        public void HideEmailDetails_NullString_ReturnsEmpty()
        {
            SecretField.HideEmailDetails(null!).Should().BeEmpty();
        }

        [Fact]
        public void HideSensitiveInfo_WithLeftRight_MasksMiddle()
        {
            var result = SecretField.HideSensitiveInfo("1234567890", left: 3, right: 2);
            result.Should().StartWith("123");
            result.Should().EndWith("90");
            result.Should().Contain("*****"); // 10 - 3 - 2 = 5 stars
        }

        [Fact]
        public void HideSensitiveInfo_EmptyString_ReturnsEmpty()
        {
            SecretField.HideSensitiveInfo("", 3, 2).Should().BeEmpty();
        }

        [Fact]
        public void HideSensitiveInfo_WithSublen_MasksCorrectly()
        {
            var result = SecretField.HideSensitiveInfo("abcdefghij", sublen: 3);
            // subLength = 10/3 = 3, prefix="abc", suffix="hij", middle="****"
            result.Should().Be("abc****hij");
        }

        [Fact]
        public void HideSensitiveInfo_ShortString_ShowsLeftWithStars()
        {
            var result = SecretField.HideSensitiveInfo("ab", sublen: 3, basedOnLeft: true);
            result.Should().Contain("****");
        }
    }
}
