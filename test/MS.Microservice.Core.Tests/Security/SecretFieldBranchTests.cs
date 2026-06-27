using FluentAssertions;
using MS.Microservice.Core.Security;

namespace MS.Microservice.Core.Tests.Security
{
    /// <summary>
    /// Tests for uncovered branches in HideSensitiveInfo and HideEmailDetails.
    /// </summary>
    public class SecretFieldBranchTests
    {
        [Fact]
        public void HideSensitiveInfo_NullOrEmpty_ReturnsEmpty()
        {
            SecretField.HideSensitiveInfo(null!, 3, 2).Should().BeEmpty();
        }

        [Fact]
        public void HideSensitiveInfo_InfoTooShort_BasedOnLeft_InfoGtLeft()
        {
            // info.Length=5, left=3, right=4 => hiddenCharCount = -2
            // basedOnLeft=true, info.Length(5) > left(3) && left(3) > 0
            var result = SecretField.HideSensitiveInfo("abcde", left: 3, right: 4, basedOnLeft: true);
            result.Should().Be("abc****");
        }

        [Fact]
        public void HideSensitiveInfo_InfoTooShort_BasedOnLeft_InfoLteLeft()
        {
            // info.Length=2, left=3, right=2 => hiddenCharCount = -3
            // basedOnLeft=true, info.Length(2) <= left(3)
            var result = SecretField.HideSensitiveInfo("ab", left: 3, right: 2, basedOnLeft: true);
            result.Should().Be("a****");
        }

        [Fact]
        public void HideSensitiveInfo_InfoTooShort_BasedOnRight_InfoGtRight()
        {
            // info.Length=5, left=4, right=3 => hiddenCharCount = -2
            // basedOnLeft=false, info.Length(5) > right(3) && right(3) > 0
            var result = SecretField.HideSensitiveInfo("abcde", left: 4, right: 3, basedOnLeft: false);
            result.Should().Be("****cde");
        }

        [Fact]
        public void HideSensitiveInfo_InfoTooShort_BasedOnRight_InfoLteRight()
        {
            // info.Length=2, left=3, right=2 => hiddenCharCount = -3
            // basedOnLeft=false, info.Length(2) <= right(2)
            var result = SecretField.HideSensitiveInfo("ab", left: 3, right: 2, basedOnLeft: false);
            result.Should().Be("****b");
        }

        [Fact]
        public void HideSensitiveInfo_TwoArg_Null_ReturnsEmpty()
        {
            SecretField.HideSensitiveInfo(null!, sublen: 3).Should().BeEmpty();
        }

        [Fact]
        public void HideSensitiveInfo_TwoArg_ShortInfo_BasedOnRight()
        {
            // info.Length=5, sublen=3 => subLength=1
            // info.Length(5) > subLength*2(2) => prefix="a", suffix="e"
            var result = SecretField.HideSensitiveInfo("abcde", sublen: 3, basedOnLeft: false);
            result.Should().Be("a****e");
        }

        [Fact]
        public void HideSensitiveInfo_TwoArg_VeryShort_BasedOnRight()
        {
            // info.Length=2, sublen=3 => subLength=0
            // info.Length(2) <= 0 => else branch, basedOnLeft=false
            // suffix = info[^1..] = "b"
            var result = SecretField.HideSensitiveInfo("ab", sublen: 3, basedOnLeft: false);
            result.Should().Be("****b");
        }

        [Fact]
        public void HideSensitiveInfo_TwoArg_SublenOne_DefaultsToThree()
        {
            // sublen=1 => becomes 3
            var result = SecretField.HideSensitiveInfo("abcdefghij", sublen: 1);
            result.Should().Be("abc****hij");
        }

        [Fact]
        public void HideEmailDetails_NonEmail_UsesHideSensitiveInfo()
        {
            // "notanemail" does not match email regex
            var result = SecretField.HideEmailDetails("notanemail", left: 2);
            result.Should().Contain("*");
        }

        [Fact]
        public void Phone_BoundaryLength_Works()
        {
            var act = () => SecretField.Phone("12345678901");
            act.Should().NotThrow();
        }
    }
}
