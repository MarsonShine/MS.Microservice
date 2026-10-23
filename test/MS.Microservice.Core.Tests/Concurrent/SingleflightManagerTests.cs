using MS.Microservice.Core.Concurrent;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Tests.Concurrent
{
    public class SingleflightManagerTests
    {
        [Fact]
        public async Task LegacyEntrySharesFailureAndAllowsAnotherAttempt()
        {
            var manager = new SingleflightManager();
            var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = manager.ExecuteOnceAsync("key", () => release.Task);
            var follower = manager.ExecuteOnceAsync("key", () => Task.FromResult(99));
            var failure = new System.IO.IOException("source failed");
            release.SetException(failure);
            Assert.Same(failure, await Assert.ThrowsAsync<System.IO.IOException>(() => first.WaitAsync(TimeSpan.FromSeconds(5))));
            Assert.Same(failure, await Assert.ThrowsAsync<System.IO.IOException>(() => follower.WaitAsync(TimeSpan.FromSeconds(5))));
            Assert.Equal(7, await manager.ExecuteOnceAsync("key", () => Task.FromResult(7)));
        }

        [Fact]
        public async Task ExecuteOnceAsync_ReturnsCorrectResult()
        {
            var manager = new SingleflightManager();
            var result = await manager.ExecuteOnceAsync("key1", async () =>
            {
                await Task.Delay(10);
                return 42;
            });

            Assert.Equal(42, result);
        }

        [Fact]
        public async Task ExecuteOnceAsync_ConcurrentCalls_ExecutesOnlyOnce()
        {
            var manager = new SingleflightManager();
            int executionCount = 0;

            // Use a barrier to ensure all tasks start simultaneously
            var barrier = new TaskCompletionSource<bool>();

            var tasks = new Task<string>[20];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    await barrier.Task; // Wait for all tasks to be ready
                    return await manager.ExecuteOnceAsync("same_key", async () =>
                    {
                        Interlocked.Increment(ref executionCount);
                        await Task.Delay(100);
                        return "shared_result";
                    });
                });
            }

            // Release all tasks at once
            barrier.SetResult(true);
            var results = await Task.WhenAll(tasks);

            // All tasks should get the same result
            foreach (var result in results)
            {
                Assert.Equal("shared_result", result);
            }

            // The action should have been executed only once (or very few times due to timing)
            Assert.True(executionCount <= 2, $"Expected at most 2 executions, but got {executionCount}");
        }

        [Fact]
        public async Task ExecuteOnceAsync_DifferentKeys_ExecutesSeparately()
        {
            var manager = new SingleflightManager();
            int count1 = 0;
            int count2 = 0;

            var t1 = manager.ExecuteOnceAsync("key_a", async () =>
            {
                Interlocked.Increment(ref count1);
                await Task.Delay(10);
                return "result_a";
            });

            var t2 = manager.ExecuteOnceAsync("key_b", async () =>
            {
                Interlocked.Increment(ref count2);
                await Task.Delay(10);
                return "result_b";
            });

            var results = await Task.WhenAll(t1, t2);

            Assert.Equal("result_a", results[0]);
            Assert.Equal("result_b", results[1]);
            Assert.Equal(1, count1);
            Assert.Equal(1, count2);
        }

        [Fact]
        public async Task ExecuteOnceAsync_SequentialSameKey_ExecutesBothTimes()
        {
            var manager = new SingleflightManager();
            int executionCount = 0;

            await manager.ExecuteOnceAsync("key", async () =>
            {
                Interlocked.Increment(ref executionCount);
                await Task.Delay(10);
                return "first";
            });

            await manager.ExecuteOnceAsync("key", async () =>
            {
                Interlocked.Increment(ref executionCount);
                await Task.Delay(10);
                return "second";
            });

            // Sequential calls should each execute since the first completed & removed the key
            Assert.Equal(2, executionCount);
        }
    }
}
