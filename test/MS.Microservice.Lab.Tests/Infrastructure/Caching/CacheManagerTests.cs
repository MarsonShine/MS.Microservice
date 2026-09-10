using MS.Microservice.Infrastructure.Caching;
using MS.Microservice.Infrastructure.Caching.Store;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.Caching;

public sealed class CacheManagerTests
{
    [Fact]
    public async Task SetAndExistsAsync_StoresValueAndReportsExistence()
    {
        using var manager = CreateManager();

        await manager.SetAsync("key", "value");

        Assert.True(await manager.ExistsAsync("key"));
        var item = await manager.GetAsync<string>("key");
        Assert.Equal("value", item?.Value);
    }

    [Fact]
    public async Task SetAsync_ExistingKey_ReplacesValue()
    {
        using var manager = CreateManager();
        await manager.SetAsync("key", "first");

        await manager.SetAsync("key", "second");

        var item = await manager.GetAsync<string>("key");
        Assert.Equal("second", item?.Value);
    }

    [Fact]
    public async Task RemoveAsync_RemovesValueAndMetadata()
    {
        var (manager, keyStore) = CreateManagerWithKeyStore();
        using (manager)
        {
            await manager.SetAsync("key", "value");

            await manager.RemoveAsync("key");

            Assert.False(await manager.ExistsAsync("key"));
            Assert.Null(await keyStore.GetKeyMetadataAsync("key"));
        }
    }

    [Fact]
    public async Task GetAsync_ExpiredItem_RemovesAndReturnsMiss()
    {
        using var manager = CreateManager();
        await manager.SetAsync("expired", "value", TimeSpan.Zero);

        var item = await manager.GetAsync<string>("expired");

        Assert.Null(item);
        Assert.False(await manager.ExistsAsync("expired"));
    }

    [Fact]
    public async Task GetOrAddAsync_ConcurrentMisses_InvokeFactoryOnce()
    {
        using var manager = CreateManager();
        var factoryCalls = 0;
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<string> Factory()
        {
            Interlocked.Increment(ref factoryCalls);
            return CompleteFactoryAsync();
        }

        async Task<string> CompleteFactoryAsync()
        {
            await releaseFactory.Task;
            return "created";
        }

        var requests = Enumerable.Range(0, 20)
            .Select(_ => manager.GetOrAddAsync("single-flight", Factory))
            .ToArray();
        await Task.Delay(50);
        releaseFactory.SetResult();

        var results = await Task.WhenAll(requests);

        Assert.Equal(1, factoryCalls);
        Assert.All(results, item => Assert.Equal("created", item.Value));
    }

    [Fact]
    public async Task GetOrAddAsync_NullResult_IsNegativeCached()
    {
        using var manager = CreateManager();
        var factoryCalls = 0;

        Task<string?> Factory()
        {
            factoryCalls++;
            return Task.FromResult<string?>(null);
        }

        var first = await manager.GetOrAddAsync("missing-model", Factory, TimeSpan.FromMinutes(1));
        var second = await manager.GetOrAddAsync("missing-model", Factory, TimeSpan.FromMinutes(1));

        Assert.Null(first.Value);
        Assert.Null(second.Value);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrAddAsync_WhenFactoryFails_DoesNotCacheFailureAndAllowsRetry()
    {
        using var manager = CreateManager();
        var factoryCalls = 0;

        async Task<string> Factory()
        {
            factoryCalls++;
            await Task.Yield();
            if (factoryCalls == 1)
            {
                throw new InvalidOperationException("factory failed");
            }

            return "recovered";
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.GetOrAddAsync("retry", Factory));
        var recovered = await manager.GetOrAddAsync("retry", Factory);

        Assert.Equal("factory failed", exception.Message);
        Assert.Equal("recovered", recovered.Value);
        Assert.Equal(2, factoryCalls);
    }

    private static CacheManager CreateManager()
        => CreateManagerWithKeyStore().Manager;

    private static (CacheManager Manager, IKeyStore KeyStore) CreateManagerWithKeyStore()
    {
        var keyStoreType = typeof(InMemoryCacheStore).Assembly
            .GetType("MS.Microservice.Infrastructure.Caching.Store.InMemoryKeyStore")!;
        var keyStore = (IKeyStore)Activator.CreateInstance(keyStoreType)!;
        var cacheStore = new InMemoryCacheStore(keyStore);
        var manager = new CacheManager(
            keyStore,
            cacheStore,
            new CacheOperationLogOptions { EnableOperationLog = false });
        return (manager, keyStore);
    }
}
