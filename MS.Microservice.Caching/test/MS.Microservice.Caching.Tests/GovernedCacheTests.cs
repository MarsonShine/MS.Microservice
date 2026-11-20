using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Caching;
using Xunit;

public sealed class GovernedCacheTests
{
    [Fact]
    public async Task EmptyNullAndDefaultValuesAreCachedAndKeysIsolateScopeTypeAndSeparators()
    {
        using var services = Services(); var cache = services.GetRequiredService<IGovernedCache>();
        await Check(cache.CreateKey<string?>("test.v1", "owner", "null"), null);
        await Check(cache.CreateKey<string>("test.v1", "owner", "empty"), "");
        await Check(cache.CreateKey<int>("test.v1", "owner", "zero"), 0);
        await Check(cache.CreateKey<bool>("test.v1", "owner", "false"), false);
        await Check(cache.CreateKey<int[]>("test.v1", "owner", "array"), []);
        Assert.NotEqual(cache.CreateKey<int>("test.v1", "a:b", "c").Value, cache.CreateKey<int>("test.v1", "a", "b:c").Value);
        Assert.NotEqual(cache.CreateKey<int>("test.v1", "a", "id").Value, cache.CreateKey<string>("test.v1", "a", "id").Value);
        Assert.NotEqual(cache.CreateKey<int>("test.v1", "A", "id").Value, cache.CreateKey<int>("test.v1", "a", "id").Value);
        async Task Check<T>(CacheKey<T> key, T value)
        {
            var calls = 0;
            ValueTask<T> Load(CancellationToken _) { calls++; return ValueTask.FromResult(value); }
            await cache.GetOrCreateAsync(key, Load); await cache.GetOrCreateAsync(key, Load);
            Assert.Equal(1, calls);
        }
    }
    [Fact]
    public async Task ConcurrentMissesCoalesceAndRemoveReloadsWithoutCachingFailures()
    {
        using var services = Services(); var cache = services.GetRequiredService<IGovernedCache>();
        var key = cache.CreateKey<string>("test.v1", "scope", "item");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        async ValueTask<string> Load(CancellationToken token) { Interlocked.Increment(ref calls); await release.Task.WaitAsync(token); return "value"; }
        var reads = Enumerable.Range(0, 32).Select(_ => cache.GetOrCreateAsync(key, Load).AsTask()).ToArray();
        release.SetResult(); Assert.All(await Task.WhenAll(reads), value => Assert.Equal("value", value)); Assert.Equal(1, calls);
        await cache.RemoveAsync(key);
        await Assert.ThrowsAsync<IOException>(() => cache.GetOrCreateAsync(key, _ => ValueTask.FromException<string>(new IOException())).AsTask());
        Assert.Equal("value", await cache.GetOrCreateAsync(key, Load)); Assert.Equal(2, calls);
    }
    [Fact]
    public async Task LoadBudgetBoundsDistinctMissesAndReleasesAfterFailureCancellationAndBypass()
    {
        using var services = Services(options => options.MaximumConcurrentLoads = 1);
        var cache = services.GetRequiredService<IGovernedCache>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = cache.GetOrCreateAsync(cache.CreateKey<int>("test.v1", "s", "one"), async token =>
        { entered.SetResult(); await release.Task.WaitAsync(token); throw new IOException(); }).AsTask();
        await entered.Task;
        await Assert.ThrowsAsync<CacheLoadLimitException>(() => cache.GetOrCreateAsync(cache.CreateKey<int>("test.v1", "s", "two"), _ => ValueTask.FromResult(2)).AsTask());
        release.SetResult(); await Assert.ThrowsAsync<IOException>(() => first);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => cache.GetOrCreateAsync(cache.CreateKey<int>("test.v1", "s", "canceled"), _ => ValueTask.FromResult(0), cancellation.Token));
        Assert.Equal(3, await cache.GetOrCreateAsync(cache.CreateKey<int>("test.v1", "s", "three"), _ => ValueTask.FromResult(3)));
        var policy = CachePolicy.Bypass; var bypassKey = cache.CreateKey<int>("test.v1", "s", "bypass", policy);
        var calls = 0;
        for (var index = 0; index < 2; index++) Assert.Equal(++calls, await cache.GetOrCreateAsync(bypassKey, calls, static (value, _) => ValueTask.FromResult(value)));
    }
    [Fact]
    public async Task DisabledRegistrationSkipsBackendAndInvalidPolicyAndForeignKeysAreRejected()
    {
        using var services = Services(options => options.Enabled = false);
        Assert.Null(services.GetService<HybridCache>());
        var cache = services.GetRequiredService<IGovernedCache>(); var key = cache.CreateKey<int>("test.v1", "s", "id");
        var calls = 0;
        for (var index = 0; index < 2; index++) await cache.GetOrCreateAsync(key, _ => ValueTask.FromResult(++calls));
        Assert.Equal(2, calls);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CachePolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        using var other = Services(options => options.ApplicationName = "other");
        Assert.Throws<ArgumentException>(() => other.GetRequiredService<IGovernedCache>().GetOrCreateAsync(key, _ => ValueTask.FromResult(0)));
    }
    private static ServiceProvider Services(Action<CachingOptions>? configure = null)
    { var services = new ServiceCollection(); services.AddGovernedCaching(configure); return services.BuildServiceProvider(); }
}
