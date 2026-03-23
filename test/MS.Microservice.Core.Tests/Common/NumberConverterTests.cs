using FluentAssertions;
using MS.Microservice.Core.Common;

namespace MS.Microservice.Core.Tests.Common
{
    public class NumberConverterTests
    {
        [Theory]
        [InlineData(0, "0")]
        [InlineData(1, "1")]
        [InlineData(10, "1010")]
        [InlineData(255, "11111111")]
        [InlineData(-1, "11111111111111111111111111111111")]
        public void DecimalToBinary_ConvertsCorrectly(int input, string expected)
        {
            NumberConverter.DecimalToBinary(input).Should().Be(expected);
        }

        [Theory]
        [InlineData("0", 0)]
        [InlineData("1", 1)]
        [InlineData("1010", 10)]
        [InlineData("11111111", 255)]
        public void BinaryToDecimal_ConvertsCorrectly(string input, int expected)
        {
            NumberConverter.BinaryToDecimal(input).Should().Be(expected);
        }

        [Theory]
        [InlineData(1, 1, 2)]
        [InlineData(1, 3, 8)]
        [InlineData(5, 2, 20)]
        public void LeftShift_ShiftsCorrectly(int number, int shift, int expected)
        {
            NumberConverter.LeftShift(number, shift).Should().Be(expected);
        }

        [Theory]
        [InlineData(8, 2, 2)]
        [InlineData(20, 2, 5)]
        public void RightShift_ArithmeticShift_PreservesSign(int number, int shift, int expected)
        {
            NumberConverter.RightShift(number, shift).Should().Be(expected);
        }

        [Fact]
        public void RightShift_NegativeNumber_PreservesSignBit()
        {
            // 算术右移，负数左边补1
            int result = NumberConverter.RightShift(-8, 2);
            result.Should().Be(-2);
        }

        [Fact]
        public void RightLogicShift_NegativeNumber_DoesNotPreserveSign()
        {
            // 逻辑右移，负数左边补0，结果为正数
            int result = NumberConverter.RightLogicShift(-8, 2);
            result.Should().BePositive();
        }

        [Fact]
        public void RoundTrip_DecimalBinaryDecimal()
        {
            int original = 42;
            string binary = NumberConverter.DecimalToBinary(original);
            int roundTrip = NumberConverter.BinaryToDecimal(binary);
            roundTrip.Should().Be(original);
        }
    }
}
