using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Caching;
using Xunit;

public sealed class SharedCacheTests
{
    [Fact]
    public async Task SeparateInstancesUseExplicitJsonMetadataAndLocalEntriesExpire()
    {
        var backend = new SharedBackend();
        using var first = Services(backend); using var second = Services(backend);
        var cache = first.GetRequiredService<IGovernedCache>(); var other = second.GetRequiredService<IGovernedCache>();
        var value = new SharedPayload("中文 <tag>", 0);
        var key = cache.CreateKey<SharedPayload>("shared.v1", "tenant", "item");
        await cache.SetAsync(key, value);
        var unexpectedLoads = 0;
        var loaded = await other.GetOrCreateAsync(other.CreateKey<SharedPayload>("shared.v1", "tenant", "item"), _ =>
        { unexpectedLoads++; return ValueTask.FromResult(new SharedPayload("wrong", -1)); });
        Assert.Equal(value, loaded); Assert.Equal(0, unexpectedLoads); Assert.True(backend.Reads > 0);
        var shortKey = cache.CreateKey<int>("expiry.v1", "tenant", "item", new(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)));
        var calls = 0;
        await cache.GetOrCreateAsync(shortKey, _ => ValueTask.FromResult(++calls));
        await Task.Delay(80);
        Assert.Equal(2, await cache.GetOrCreateAsync(shortKey, _ => ValueTask.FromResult(++calls)));
    }
    private static ServiceProvider Services(SharedBackend backend)
    {
        var services = new ServiceCollection(); services.AddSingleton<IDistributedCache>(backend);
        services.AddGovernedCaching(serialization: builder => builder.AddGovernedJson(CacheTestJsonContext.Default.SharedPayload));
        return services.BuildServiceProvider();
    }
    private sealed class SharedBackend : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, (byte[] Bytes, DateTimeOffset? Expires)> _data = new();
        internal int Reads;
        public byte[]? Get(string key)
        {
            Interlocked.Increment(ref Reads);
            if (!_data.TryGetValue(key, out var value) || value.Expires <= DateTimeOffset.UtcNow) return null;
            return value.Bytes;
        }
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) { token.ThrowIfCancellationRequested(); return Task.FromResult(Get(key)); }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _data[key] =
            (value.ToArray(), options.AbsoluteExpiration ?? (options.AbsoluteExpirationRelativeToNow is { } ttl ? DateTimeOffset.UtcNow + ttl : null));
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); Set(key, value, options); return Task.CompletedTask; }
        public void Remove(string key) => _data.TryRemove(key, out _);
        public Task RemoveAsync(string key, CancellationToken token = default) { token.ThrowIfCancellationRequested(); Remove(key); return Task.CompletedTask; }
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    }
}
public sealed record SharedPayload(string Name, int Count);
[JsonSerializable(typeof(SharedPayload))]
internal partial class CacheTestJsonContext : JsonSerializerContext;
