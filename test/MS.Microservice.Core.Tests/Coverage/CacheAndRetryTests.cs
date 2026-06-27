using System;
using System.Threading;
using System.Threading.Tasks;
using MS.Microservice.Core.Ceching;
using MS.Microservice.Core.Common.Advance.Resilience;
using Xunit;

namespace MS.Microservice.Core.Tests.Coverage
{
    // ===== WeakEvictionCache edge cases =====
    public class WeakEvictionCacheEdgeTests
    {
        [Fact] public void Add_Then_Get() { var c = new WeakEvictionCache<string, string>(TimeSpan.FromMinutes(5)); c.Add("k", "v"); Assert.True(c.TryGet("k", out var r)); Assert.Equal("v", r); }
        [Fact] public void Get_Missing() { var c = new WeakEvictionCache<string, string>(TimeSpan.FromMinutes(5)); Assert.False(c.TryGet("missing", out _)); }
        [Fact] public void Eviction_DoesNotRemoveStrong() { var c = new WeakEvictionCache<string, string>(TimeSpan.FromMilliseconds(1)); c.Add("k", "v"); System.Threading.Thread.Sleep(10); c.DoWeakEviction(); Assert.True(c.TryGet("k", out _)); }
        [Fact] public void Eviction_ConvertsToWeak() { var c = new WeakEvictionCache<string, string>(TimeSpan.FromMilliseconds(1)); c.Add("k", "v"); System.Threading.Thread.Sleep(20); c.DoWeakEviction(); c.TryGet("k", out _); }
        [Fact] public void Add_Null_Throws() { var c = new WeakEvictionCache<string, string>(TimeSpan.FromMinutes(5)); Assert.Throws<ArgumentNullException>(() => c.Add("k", null!)); }
    }

    // ===== RetryExecutor tests =====
    public class RetryExecutorTests
    {
        private class AlwaysRetry : IRetryStrategy
        {
            public bool ShouldRetry(RetryContext context) => context.Attempt < 3;
            public TimeSpan GetDelay(RetryContext context) => TimeSpan.FromMilliseconds(1);
        }
        private class SuccessCondition : IRetryCondition
        {
            public bool ShouldRetry<T>(T? result, Exception? exception) => false;
        }
        private class RetryCondition : IRetryCondition
        {
            public bool ShouldRetry<T>(T? result, Exception? exception) => exception != null;
        }

        [Fact] public async Task ExecuteAsync_Success() { var exec = new RetryExecutor(new AlwaysRetry(), new SuccessCondition()); var r = await exec.ExecuteAsync(() => Task.FromResult(42)); Assert.Equal(42, r); }
        [Fact] public async Task ExecuteAsync_RetryOnException() { var count = 0; var exec = new RetryExecutor(new AlwaysRetry(), new RetryCondition()); var r = await exec.ExecuteAsync<int>(() => { count++; return Task.FromResult(count < 3 ? throw new InvalidOperationException("fail") : 100); }); Assert.Equal(100, r); Assert.Equal(3, count); }
        [Fact] public async Task ExecuteAsync_Exhausted() { var exec = new RetryExecutor(new AlwaysRetry(), new RetryCondition()); await Assert.ThrowsAsync<InvalidOperationException>(() => exec.ExecuteAsync<int>(() => throw new InvalidOperationException("always fail"))); }
        [Fact] public void Logger_Property() { var exec = new RetryExecutor(new AlwaysRetry(), new SuccessCondition()); Assert.NotNull(exec.Logger); exec.Logger = null!; Assert.NotNull(exec.Logger); }
        [Fact] public void Constructor_NullStrategy_Throws() { Assert.Throws<ArgumentNullException>(() => new RetryExecutor(null!, new SuccessCondition())); }
        [Fact] public void Constructor_NullCondition_Throws() { Assert.Throws<ArgumentNullException>(() => new RetryExecutor(new AlwaysRetry(), null!)); }
        [Fact] public async Task ExecuteAsync_NullOperation_Throws() { var exec = new RetryExecutor(new AlwaysRetry(), new SuccessCondition()); await Assert.ThrowsAsync<ArgumentNullException>(() => exec.ExecuteAsync<int>(null!)); }
        [Fact] public void Execute_Sync() { var exec = new RetryExecutor(new AlwaysRetry(), new SuccessCondition()); var r = exec.Execute(() => 42); Assert.Equal(42, r); }
        [Fact] public void Execute_Action() { bool called = false; var exec = new RetryExecutor(new AlwaysRetry(), new SuccessCondition()); exec.Execute(() => { called = true; }); Assert.True(called); }
    }
}
