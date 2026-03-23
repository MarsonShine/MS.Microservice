using System.Text;
using FluentAssertions;
using MS.Microservice.Core.Extension;

namespace MS.Microservice.Core.Tests.Extensions
{
    public class StringExtensionsTests
    {
        [Fact]
        public void IsNullOrEmpty_Null_ReturnsTrue()
        {
            string? s = null;
            s.IsNullOrEmpty().Should().BeTrue();
        }

        [Fact]
        public void IsNullOrEmpty_Empty_ReturnsTrue()
        {
            "".IsNullOrEmpty().Should().BeTrue();
        }

        [Fact]
        public void IsNullOrEmpty_NonEmpty_ReturnsFalse()
        {
            "hello".IsNullOrEmpty().Should().BeFalse();
        }

        [Fact]
        public void IsNullOrWhiteSpace_Whitespace_ReturnsTrue()
        {
            "   ".IsNullOrWhiteSpace().Should().BeTrue();
        }

        [Fact]
        public void IsNotNullOrEmpty_WithValue_ReturnsTrue()
        {
            "hello".IsNotNullOrEmpty().Should().BeTrue();
        }

        [Fact]
        public void IsNotNullOrEmpty_Null_ReturnsFalse()
        {
            string? s = null;
            s.IsNotNullOrEmpty().Should().BeFalse();
        }

        [Fact]
        public void IsNotNullOrWhiteSpace_WithValue_ReturnsTrue()
        {
            "hello".IsNotNullOrWhiteSpace().Should().BeTrue();
        }

        [Fact]
        public void IsNotNullOrWhiteSpace_Whitespace_ReturnsFalse()
        {
            "  ".IsNotNullOrWhiteSpace().Should().BeFalse();
        }

        [Fact]
        public void ReadAsByte_ConvertsStringToBytes()
        {
            var bytes = "ABC".ReadAsByte(Encoding.UTF8);
            bytes.Should().Equal(Encoding.UTF8.GetBytes("ABC"));
        }
    }
}
