using System;
using System.Numerics;
using Xunit;

namespace MS.Microservice.Core.Tests.Extensions
{
    public class MathExtensionsTests
    {
        [Fact]
        public void Round_ShouldRoundCorrectly()
        {
            // Arrange
            double value = 3.14159;

            // Act & Assert
            Assert.Equal(3.0, value.Round());
            Assert.Equal(3.14, value.Round(2));
            Assert.Equal(3.1416, value.Round(4));
        }

        [Fact]
        public void Ceiling_ShouldRoundUpCorrectly()
        {
            // Arrange
            float value = 3.1f;

            // Act & Assert
            Assert.Equal(4.0f, value.Ceiling());
        }

        [Fact]
        public void Floor_ShouldRoundDownCorrectly()
        {
            // Arrange
            decimal value = 3.9m;

            // Act & Assert
            Assert.Equal(3.0m, value.Floor());
        }

        [Fact]
        public void Abs_ShouldReturnAbsoluteValue()
        {
            // Arrange
            double negative = -5.5;
            double positive = 5.5;

            // Act & Assert
            Assert.Equal(5.5, negative.Abs());
            Assert.Equal(5.5, positive.Abs());
        }

        [Fact]
        public void Clamp_ShouldConstrainValue()
        {
            // Arrange
            int value = 15;
            int min = 10;
            int max = 20;

            // Act & Assert
            Assert.Equal(15, value.Clamp(min, max)); // 在范围内
            Assert.Equal(10, 5.Clamp(min, max));    // 小于最小值
            Assert.Equal(20, 25.Clamp(min, max));   // 大于最大值
        }

        [Fact]
        public void Clamp_ShouldThrowWhenMinGreaterThanMax()
        {
            // Arrange
            double value = 5.0;

            // Act & Assert
            Assert.Throws<ArgumentException>(() => value.Clamp(10.0, 5.0));
        }

        [Theory]
        [InlineData(1.4, 1.0)]
        [InlineData(1.5, 2.0)]
        [InlineData(1.6, 2.0)]
        [InlineData(-1.5, -2.0)]
        public void Round_WithMidpointRounding_ShouldRoundCorrectly(double input, double expected)
        {
            // Act & Assert
            Assert.Equal(expected, input.Round(MidpointRounding.AwayFromZero));
        }
    }
}