using MS.Microservice.Core.Functional;
using Xunit;

namespace MS.Microservice.Core.Tests.Functional
{
    public class FAsyncTests
    {
        [Fact]
        public async Task Async_WrapsValueInTask()
        {
            var result = await F.Async(42);
            Assert.Equal(42, result);
        }

        [Fact]
        public async Task Map_TransformsValue()
        {
            var result = await Task.FromResult(21).Map(x => x * 2);
            Assert.Equal(42, result);
        }

        [Fact]
        public async Task Bind_AsyncTransformsValue()
        {
            var result = await Task.FromResult(21).Bind(x => Task.FromResult(x * 2));
            Assert.Equal(42, result);
        }

        [Fact]
        public async Task OrElse_WhenSuccess_ReturnsOriginal()
        {
            var result = await Task.FromResult(42).OrElse(() => Task.FromResult(99));
            Assert.Equal(42, result);
        }

        [Fact]
        public async Task OrElse_WhenThrows_UsesFallback()
        {
            var result = await Task.FromException<int>(new InvalidOperationException("boom"))
                .OrElse(() => Task.FromResult(99));
            Assert.Equal(99, result);
        }

        [Fact]
        public async Task Recover_WhenFaulted_UsesFallback()
        {
            var result = await Task.FromException<int>(new InvalidOperationException("boom"))
                .Recover(ex => 99);
            Assert.Equal(99, result);
        }

        [Fact]
        public async Task Recover_WhenSuccess_KeepsValue()
        {
            var result = await Task.FromResult(42).Recover(ex => 99);
            Assert.Equal(42, result);
        }

        [Fact]
        public async Task Map_FaultedCompleted_OnCompleted()
        {
            var result = await Task.FromResult(21).Map(
                Faulted: ex => 0,
                Completed: x => x * 2);
            Assert.Equal(42, result);
        }

        [Fact]
        public async Task Map_FaultedCompleted_OnFaulted()
        {
            var result = await Task.FromException<int>(new InvalidOperationException()).Map(
                Faulted: ex => 99,
                Completed: x => x * 2);
            Assert.Equal(99, result);
        }

        [Fact]
        public async Task Select_TransformsValue()
        {
            var result = await Task.FromResult(21).Select(x => x * 2);
            Assert.Equal(42, result);
        }

        [Fact]
        public async Task Apply_UnaryFunc()
        {
            Task<Func<int, int>> f = Task.FromResult((Func<int, int>)(x => x * 2));
            var result = await f.Apply(Task.FromResult(21));
            Assert.Equal(42, result);
        }

        [Fact]
        public async Task Retry_SucceedsOnFirstTry()
        {
            var result = await F.Retry<int>(3, 10, () => Task.FromResult(42));
            Assert.Equal(42, result);
        }

        [Fact]
        public async Task Retry_RetriesOnFailure()
        {
            int attempts = 0;
            var result = await F.Retry<int>(3, 10, () =>
            {
                attempts++;
                if (attempts < 3)
                    throw new InvalidOperationException("fail");
                return Task.FromResult(42);
            });
            Assert.Equal(42, result);
            Assert.Equal(3, attempts);
        }

        [Fact]
        public async Task Retry_ExhaustsRetries_ThrowsLast()
        {
            int attempts = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                F.Retry<int>(2, 10, () =>
                {
                    attempts++;
                    throw new InvalidOperationException("always fail");
                }));
            Assert.Equal(3, attempts);
        }
    }
}
