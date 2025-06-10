using MS.Microservice.Core.Common.Advance.Resilience.RetryStrategy;
using System;

namespace MS.Microservice.Core.Tests.Common.Advance.Resilience
{
    public class RetryStrategyTests
    {
        [Fact]
        public void FixedCountRetryStrategy_ShouldRetry_ReturnsTrueWhenAttemptLessThanMaxRetries()
        {
            // Arrange
            var strategy = new FixedCountRetryStrategy(3, TimeSpan.FromMilliseconds(100));

            // Act & Assert
            Assert.True(strategy.ShouldRetry(1, null));
            Assert.True(strategy.ShouldRetry(2, null));
            Assert.True(strategy.ShouldRetry(3, null));
            Assert.False(strategy.ShouldRetry(4, null));
        }

        [Fact]
        public void FixedCountRetryStrategy_GetDelay_ReturnsConstantDelay()
        {
            // Arrange
            var expectedDelay = TimeSpan.FromMilliseconds(100);
            var strategy = new FixedCountRetryStrategy(3, expectedDelay);

            // Act & Assert
            Assert.Equal(expectedDelay, strategy.GetDelay(1, null));
            Assert.Equal(expectedDelay, strategy.GetDelay(2, null));
            Assert.Equal(expectedDelay, strategy.GetDelay(3, null));
        }

        [Fact]
        public void ExponentialBackoffRetryStrategy_ShouldRetry_ReturnsTrueWhenAttemptLessThanMaxRetries()
        {
            // Arrange
            var strategy = new ExponentialBackoffRetryStrategy(3, TimeSpan.FromMilliseconds(100));

            // Act & Assert
            Assert.True(strategy.ShouldRetry(1, null));
            Assert.True(strategy.ShouldRetry(2, null));
            Assert.True(strategy.ShouldRetry(3, null));
            Assert.False(strategy.ShouldRetry(4, null));
        }

        [Fact]
        public void ExponentialBackoffRetryStrategy_GetDelay_ReturnsExponentialDelay()
        {
            // Arrange
            var baseDelay = TimeSpan.FromMilliseconds(100);
            var strategy = new ExponentialBackoffRetryStrategy(3, baseDelay, 2.0);

            // Act & Assert
            Assert.Equal(TimeSpan.FromMilliseconds(100), strategy.GetDelay(1, null));
            Assert.Equal(TimeSpan.FromMilliseconds(200), strategy.GetDelay(2, null));
            Assert.Equal(TimeSpan.FromMilliseconds(400), strategy.GetDelay(3, null));
        }

        [Fact]
        public void ExponentialBackoffRetryStrategy_GetDelay_RespectsMaxDelay()
        {
            // Arrange
            var baseDelay = TimeSpan.FromMilliseconds(100);
            var maxDelay = TimeSpan.FromMilliseconds(150);
            var strategy = new ExponentialBackoffRetryStrategy(3, baseDelay, 2.0, maxDelay);

            // Act & Assert
            Assert.Equal(TimeSpan.FromMilliseconds(100), strategy.GetDelay(1, null));
            Assert.Equal(TimeSpan.FromMilliseconds(150), strategy.GetDelay(2, null));
            Assert.Equal(TimeSpan.FromMilliseconds(150), strategy.GetDelay(3, null));
        }

        [Fact]
        public void TimeoutRetryStrategy_ShouldRetry_ReturnsTrueWhenWithinTimeout()
        {
            // Arrange
            var strategy = new TimeoutRetryStrategy(TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(100));

            // Act & Assert
            Assert.True(strategy.ShouldRetry(1, null));
        }

        [Fact]
        public void TimeoutRetryStrategy_GetDelay_ReturnsConstantDelay()
        {
            // Arrange
            var expectedDelay = TimeSpan.FromMilliseconds(100);
            var strategy = new TimeoutRetryStrategy(TimeSpan.FromSeconds(60), expectedDelay);

            // Act & Assert
            Assert.Equal(expectedDelay, strategy.GetDelay(1, null));
            Assert.Equal(expectedDelay, strategy.GetDelay(10, null));
        }
    }
}