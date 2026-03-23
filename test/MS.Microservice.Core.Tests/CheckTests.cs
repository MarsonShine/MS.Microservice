using System;
using System.Collections.Generic;
using FluentAssertions;

namespace MS.Microservice.Core.Tests
{
    public class CheckTests
    {
        [Fact]
        public void NotNull_WithNonNullValue_ReturnsValue()
        {
            var result = Check.NotNull("hello", "param");
            result.Should().Be("hello");
        }

        [Fact]
        public void NotNull_WithNullValue_ThrowsArgumentNullException()
        {
            string? value = null;
            var act = () => Check.NotNull(value!, "myParam");
            act.Should().Throw<ArgumentNullException>().WithParameterName("myParam");
        }

        [Fact]
        public void NotNull_WithMessage_ThrowsWithMessage()
        {
            object? value = null;
            var act = () => Check.NotNull(value!, "param", "custom message");
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("param")
                .WithMessage("*custom message*");
        }

        [Fact]
        public void NotNull_String_WithinLengthBounds_ReturnsValue()
        {
            var result = Check.NotNull("abc", "param", maxLength: 5, minLength: 1);
            result.Should().Be("abc");
        }

        [Fact]
        public void NotNull_String_ExceedsMaxLength_ThrowsArgumentException()
        {
            var act = () => Check.NotNull("toolong", "param", maxLength: 3);
            act.Should().Throw<ArgumentException>().WithParameterName("param");
        }

        [Fact]
        public void NotNull_String_BelowMinLength_ThrowsArgumentException()
        {
            var act = () => Check.NotNull("ab", "param", maxLength: 100, minLength: 5);
            act.Should().Throw<ArgumentException>().WithParameterName("param");
        }

        [Fact]
        public void NotNull_String_Null_ThrowsArgumentException()
        {
            string? value = null;
            var act = () => Check.NotNull(value!, "param", maxLength: 10);
            act.Should().Throw<ArgumentException>().WithParameterName("param");
        }

        [Fact]
        public void NotNullOrEmpty_WithItems_ReturnsCollection()
        {
            ICollection<int> list = new List<int> { 1, 2, 3 };
            var result = Check.NotNullOrEmpty(list, "param");
            result.Should().HaveCount(3);
        }

        [Fact]
        public void NotNullOrEmpty_WithEmptyCollection_ThrowsArgumentException()
        {
            ICollection<int> list = new List<int>();
            var act = () => Check.NotNullOrEmpty(list, "param");
            act.Should().Throw<ArgumentException>().WithParameterName("param");
        }
    }
}
