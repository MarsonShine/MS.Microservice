using MS.Microservice.Core.Common.Advance.Resilience.RetryCondition;
using System;
using System.IO;
using Xunit;

namespace MS.Microservice.Core.Tests.Common.Advance.Resilience
{
    public class RetryConditionTests
    {
        [Fact]
        public void ExceptionTypeRetryCondition_ShouldRetry_ReturnsTrueForMatchingException()
        {
            // Arrange
            var condition = new ExceptionTypeRetryCondition(typeof(InvalidOperationException));
            var exception = new InvalidOperationException("Test exception");

            // Act & Assert
            Assert.True(condition.ShouldRetry<object>(null!, exception));
        }

        [Fact]
        public void ExceptionTypeRetryCondition_ShouldRetry_ReturnsTrueForDerivedExceptions()
        {
            // Arrange
            var condition = new ExceptionTypeRetryCondition(typeof(Exception));
            var exception = new InvalidOperationException("Test exception");

            // Act & Assert
            Assert.True(condition.ShouldRetry<object>(null!, exception));
        }

        [Fact]
        public void ExceptionTypeRetryCondition_ShouldRetry_ReturnsFalseForNonMatchingException()
        {
            // Arrange
            var condition = new ExceptionTypeRetryCondition(typeof(InvalidOperationException));
            var exception = new ArgumentException("Test exception");

            // Act & Assert
            Assert.False(condition.ShouldRetry<object>(null!, exception));
        }

        [Fact]
        public void ExceptionTypeRetryCondition_ShouldRetry_ReturnsFalseForNullException()
        {
            // Arrange
            var condition = new ExceptionTypeRetryCondition(typeof(InvalidOperationException));

            // Act & Assert
            Assert.False(condition.ShouldRetry<object>(null!, null));
        }

        [Fact]
        public void ExceptionTypeRetryCondition_ShouldRetry_ReturnsTrueForMultipleExceptionTypes()
        {
            // Arrange
            var condition = new ExceptionTypeRetryCondition(typeof(InvalidOperationException), typeof(ArgumentException));
            var exception1 = new InvalidOperationException("Test exception");
            var exception2 = new ArgumentException("Test exception");

            // Act & Assert
            Assert.True(condition.ShouldRetry<object>(null!, exception1));
            Assert.True(condition.ShouldRetry<object>(null!, exception2));
        }

        [Fact]
        public void ResultConditionRetryCondition_ShouldRetry_ReturnsTrueWhenConditionMatches()
        {
            // Arrange
            var condition = new ResultConditionRetryCondition<int>(result => result < 0);

            // Act & Assert
            Assert.True(condition.ShouldRetry(-1, null));
            Assert.False(condition.ShouldRetry(0, null));
            Assert.False(condition.ShouldRetry(1, null));
        }

        [Fact]
        public void RegexMatchRetryCondition_ShouldRetry_ReturnsTrueWhenPatternMatches()
        {
            // Arrange
            var pattern = @"error|exception";
            var condition = new RegexMatchRetryCondition(pattern, true);

            // Act & Assert
            Assert.True(condition.ShouldRetry("this contains an error", null));
            Assert.True(condition.ShouldRetry("exception found", null));
            Assert.False(condition.ShouldRetry("success", null));
        }

        [Fact]
        public void RegexMatchRetryCondition_ShouldRetry_ReturnsFalseWhenPatternMatchesWithInvertedLogic()
        {
            // Arrange
            var pattern = @"error|exception";
            var condition = new RegexMatchRetryCondition(pattern, false);

            // Act & Assert
            Assert.False(condition.ShouldRetry("this contains an error", null));
            Assert.False(condition.ShouldRetry("exception found", null));
            Assert.True(condition.ShouldRetry("success", null));
        }

        [Fact]
        public void RegexMatchRetryCondition_ShouldRetry_ReturnsFalseForNonStringResult()
        {
            // Arrange
            var pattern = @"error|exception";
            var condition = new RegexMatchRetryCondition(pattern);

            // Act & Assert
            Assert.False(condition.ShouldRetry(123, null));
            Assert.False(condition.ShouldRetry(new object(), null));
        }
    }
}