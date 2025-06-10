using MS.Microservice.Core.Common.Advance.Resilience;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Tests.Common.Advance.Resilience
{
    public class RetryBuilderTests
    {
        [Fact]
        public void Create_ReturnsNewBuilder()
        {
            // Act
            var builder = RetryBuilder.Create();

            // Assert
            Assert.NotNull(builder);
        }

        [Fact]
        public void WithFixedCount_BuildsCorrectExecutor()
        {
            // Arrange
            int maxRetries = 3;
            TimeSpan delay = TimeSpan.FromMilliseconds(100);
            int attempts = 0;

            // Act
            var executor = RetryBuilder.Create()
                .WithFixedCount(maxRetries, delay)
                .OnException<InvalidOperationException>()
                .Build();

            // Assert - run a test operation
            var result = executor.Execute(() =>
            {
                attempts++;
                if (attempts <= maxRetries)
                {
                    throw new InvalidOperationException("Test exception");
                }
                return 42;
            });

            Assert.Equal(42, result);
            Assert.Equal(maxRetries + 1, attempts);
        }

        [Fact]
        public void WithExponentialBackoff_BuildsCorrectExecutor()
        {
            // Arrange
            int maxRetries = 3;
            TimeSpan baseDelay = TimeSpan.FromMilliseconds(50);
            int attempts = 0;

            // Act
            var executor = RetryBuilder.Create()
                .WithExponentialBackoff(maxRetries, baseDelay)
                .OnException<InvalidOperationException>()
                .Build();

            // Assert - run a test operation
            var result = executor.Execute(() =>
            {
                attempts++;
                if (attempts <= maxRetries)
                {
                    throw new InvalidOperationException("Test exception");
                }
                return 42;
            });

            Assert.Equal(42, result);
            Assert.Equal(maxRetries + 1, attempts);
        }

        [Fact]
        public void WithTimeout_BuildsCorrectExecutor()
        {
            // Arrange
            TimeSpan timeout = TimeSpan.FromMilliseconds(500);
            TimeSpan delay = TimeSpan.FromMilliseconds(50);
            int attempts = 0;

            // Act
            var executor = RetryBuilder.Create()
                .WithTimeout(timeout, delay)
                .OnException<InvalidOperationException>()
                .Build();

            // Assert - run a test operation
            var result = executor.Execute(() =>
            {
                attempts++;
                if (attempts <= 3) // 尝试3次，确保在超时前能完成
                {
                    throw new InvalidOperationException("Test exception");
                }
                return 42;
            });

            Assert.Equal(42, result);
            Assert.Equal(4, attempts);
        }

        [Fact]
        public void WithStrategy_CustomStrategy_BuildsCorrectExecutor()
        {
            // Arrange
            var mockStrategy = new MockRetryStrategy(3);
            int attempts = 0;

            // Act
            var executor = RetryBuilder.Create()
                .WithStrategy(mockStrategy)
                .OnException<InvalidOperationException>()
                .Build();

            // Assert - run a test operation
            var result = executor.Execute(() =>
            {
                attempts++;
                if (attempts <= 3)
                {
                    throw new InvalidOperationException("Test exception");
                }
                return 42;
            });

            Assert.Equal(42, result);
            Assert.Equal(4, attempts);
        }

        [Fact]
        public void WithCondition_CustomCondition_BuildsCorrectExecutor()
        {
            // Arrange
            var mockStrategy = new MockRetryStrategy(3);
            var mockCondition = new MockRetryCondition(true);
            int attempts = 0;

            // Act
            var executor = RetryBuilder.Create()
                .WithStrategy(mockStrategy)
                .WithCondition(mockCondition)
                .Build();

            // Assert - run a test operation
            var result = executor.Execute(() =>
            {
                attempts++;
                if (attempts <= 3)
                {
                    throw new InvalidOperationException("Test exception");
                }
                return 42;
            });

            Assert.Equal(42, result);
            Assert.Equal(4, attempts);
        }

        [Fact]
        public void OnExceptions_MultipleExceptionTypes_BuildsCorrectExecutor()
        {
            // Arrange
            int maxRetries = 3;
            int attempts = 0;

            // Act
            var executor = RetryBuilder.Create()
                .WithFixedCount(maxRetries)
                .OnExceptions(typeof(InvalidOperationException), typeof(HttpRequestException))
                .Build();

            // Assert - test with multiple exception types
            var result = executor.Execute(() =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("First exception");
                }
                if (attempts == 2)
                {
                    throw new HttpRequestException("Second exception");
                }
                if (attempts == 3)
                {
                    throw new InvalidOperationException("Third exception");
                }
                return 42;
            });

            Assert.Equal(42, result);
            Assert.Equal(4, attempts);
        }

        [Fact]
        public void OnResult_RetriesUntilConditionMet()
        {
            // Arrange
            int maxRetries = 5;
            int attempts = 0;

            // Act
            var executor = RetryBuilder.Create()
                .WithFixedCount(maxRetries)
                .OnResult<int>(result => result < 3)
                .Build();

            // Assert - test result-based condition
            var result = executor.Execute(() => ++attempts);

            Assert.Equal(3, result);
            Assert.Equal(3, attempts);
        }

        [Fact]
        public void OnRegexMatch_RetriesWhenPatternMatches()
        {
            // Arrange
            int maxRetries = 3;
            int attempts = 0;
            var pattern = "error|exception";

            // Act
            var executor = RetryBuilder.Create()
                .WithFixedCount(maxRetries)
                .OnRegexMatch(pattern, true)
                .Build();

            // Assert - test regex-based condition
            var result = executor.Execute(() =>
            {
                attempts++;
                if (attempts <= 2)
                {
                    return "Operation failed with error";
                }
                return "Success";
            });

            Assert.Equal("Success", result);
            Assert.Equal(3, attempts);
        }

        [Fact]
        public void OnRegexMatch_DoesNotRetryWhenPatternDoesNotMatch()
        {
            // Arrange
            int maxRetries = 3;
            int attempts = 0;
            var pattern = "error|exception";

            // Act
            var executor = RetryBuilder.Create()
                .WithFixedCount(maxRetries)
                .OnRegexMatch(pattern, true)
                .Build();

            // Assert - test regex-based condition with non-matching result
            var result = executor.Execute(() =>
            {
                attempts++;
                return "Success";
            });

            Assert.Equal("Success", result);
            Assert.Equal(1, attempts); // 无匹配，不重试
        }

        [Fact]
        public async Task RetryBuilder_SupportsAsyncOperations()
        {
            // Arrange
            int maxRetries = 3;
            int attempts = 0;

            // Act
            var executor = RetryBuilder.Create()
                .WithFixedCount(maxRetries)
                .OnException<InvalidOperationException>()
                .Build();

            // Assert - test with async operation
            var result = await executor.ExecuteAsync(async () =>
            {
                attempts++;
                await Task.Delay(10); // 模拟异步操作
                if (attempts <= maxRetries)
                {
                    throw new InvalidOperationException("Test exception");
                }
                return 42;
            });

            Assert.Equal(42, result);
            Assert.Equal(maxRetries + 1, attempts);
        }

        [Fact]
        public void Build_WithoutStrategy_ThrowsInvalidOperationException()
        {
            // Arrange
            var builder = RetryBuilder.Create().OnException<InvalidOperationException>();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Fact]
        public void Build_WithoutCondition_ThrowsInvalidOperationException()
        {
            // Arrange
            var builder = RetryBuilder.Create().WithFixedCount(3);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        // 辅助测试类
        private class MockRetryStrategy(int maxRetries) : IRetryStrategy
        {
            private readonly int _maxRetries = maxRetries;

            public TimeSpan GetDelay(RetryContext context) => TimeSpan.FromMilliseconds(10);

            public bool ShouldRetry(RetryContext context) => context.Attempt <= _maxRetries;
        }

        private class MockRetryCondition(bool shouldRetry) : IRetryCondition
        {
            private readonly bool _shouldRetry = shouldRetry;

            public bool ShouldRetry<T>(T result, Exception? exception) => exception != null && _shouldRetry;
        }
    }
}