using MS.Microservice.Core.Common.Advance.Resilience;
using MS.Microservice.Core.Common.Advance.Resilience.RetryCondition;
using MS.Microservice.Core.Common.Advance.Resilience.RetryStrategy;
using System;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Tests.Common.Advance.Resilience
{
    public class RetryExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_SuccessOnFirstTry_ReturnsResult()
        {
            // Arrange
            var strategy = new FixedCountRetryStrategy(3, TimeSpan.FromMilliseconds(10));
            var condition = new ExceptionTypeRetryCondition(typeof(Exception));
            var executor = new RetryExecutor(strategy, condition);

            // Act
            var result = await executor.ExecuteAsync(() => Task.FromResult(42));

            // Assert
            Assert.Equal(42, result);
        }

        [Fact]
        public async Task ExecuteAsync_SuccessAfterRetries_ReturnsResult()
        {
            // Arrange
            var strategy = new FixedCountRetryStrategy(3, TimeSpan.FromMilliseconds(10));
            var condition = new ExceptionTypeRetryCondition(typeof(Exception));
            var executor = new RetryExecutor(strategy, condition);

            int attempts = 0;

            // Act
            var result = await executor.ExecuteAsync(() =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new InvalidOperationException("Simulated failure");
                }
                return Task.FromResult(42);
            });

            // Assert
            Assert.Equal(42, result);
            Assert.Equal(3, attempts);
        }

        [Fact]
        public async Task ExecuteAsync_ExhaustsRetries_ThrowsException()
        {
            // Arrange
            var strategy = new FixedCountRetryStrategy(2, TimeSpan.FromMilliseconds(10));
            var condition = new ExceptionTypeRetryCondition(typeof(InvalidOperationException));
            var executor = new RetryExecutor(strategy, condition);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(() =>
                Task.FromException<int>(new InvalidOperationException("Simulated failure"))
            ));
        }

        [Fact]
        public async Task ExecuteAsync_UnmatchedExceptionType_ThrowsOriginalException()
        {
            // Arrange
            var strategy = new FixedCountRetryStrategy(3, TimeSpan.FromMilliseconds(10));
            var condition = new ExceptionTypeRetryCondition(typeof(InvalidOperationException));
            var executor = new RetryExecutor(strategy, condition);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => executor.ExecuteAsync(() =>
                Task.FromException<int>(new ArgumentException("Wrong argument"))
            ));
        }

        [Fact]
        public async Task ExecuteAsync_ResultCondition_RetriesUntilConditionMet()
        {
            // Arrange
            var strategy = new FixedCountRetryStrategy(5, TimeSpan.FromMilliseconds(10));
            var condition = new ResultConditionRetryCondition<int>(result => result < 3);
            var executor = new RetryExecutor(strategy, condition);

            int attempts = 0;

            // Act
            var result = await executor.ExecuteAsync(() =>
            {
                attempts++;
                return Task.FromResult(attempts);
            });

            // Assert
            Assert.Equal(3, result);
            Assert.Equal(3, attempts);
        }

        [Fact]
        public async Task ExecuteAsync_ActionOverload_ExecutesSuccessfully()
        {
            // Arrange
            var strategy = new FixedCountRetryStrategy(3, TimeSpan.FromMilliseconds(10));
            var condition = new ExceptionTypeRetryCondition(typeof(Exception));
            var executor = new RetryExecutor(strategy, condition);

            bool executed = false;

            // Act
            await executor.ExecuteAsync(() =>
            {
                executed = true;
                return Task.CompletedTask;
            });

            // Assert
            Assert.True(executed);
        }

        [Fact]
        public void Execute_SucceedsAfterRetries()
        {
            // Arrange
            var strategy = new FixedCountRetryStrategy(3, TimeSpan.FromMilliseconds(10));
            var condition = new ExceptionTypeRetryCondition(typeof(Exception));
            var executor = new RetryExecutor(strategy, condition);

            int attempts = 0;

            // Act
            var result = executor.Execute(() =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new InvalidOperationException("Simulated failure");
                }
                return 42;
            });

            // Assert
            Assert.Equal(42, result);
            Assert.Equal(3, attempts);
        }

        [Fact]
        public void Execute_ActionOverload_ExecutesSuccessfully()
        {
            // Arrange
            var strategy = new FixedCountRetryStrategy(3, TimeSpan.FromMilliseconds(10));
            var condition = new ExceptionTypeRetryCondition(typeof(Exception));
            var executor = new RetryExecutor(strategy, condition);

            bool executed = false;

            // Act
            executor.Execute(() => executed = true);

            // Assert
            Assert.True(executed);
        }
    }
}