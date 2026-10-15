using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Caching;
using MS.Microservice.Caching.Redis;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

public sealed class RedisIntegrationTests(ITestOutputHelper output)
{
    [RedisFact]
    public async Task SeparateInstancesShareRedisWhileLocalCopiesExpireIndependently()
    {
        var connection = Environment.GetEnvironmentVariable("MS_CACHE_REDIS_TEST_CONNECTION")!;
        var prefix = (Environment.GetEnvironmentVariable("MS_CACHE_REDIS_TEST_PREFIX") ?? "integration-test:")
            + Guid.NewGuid().ToString("N") + ":";
        using var first = Services(connection, prefix);
        using var second = Services(connection, prefix);
        var cache = first.GetRequiredService<IGovernedCache>();
        var other = second.GetRequiredService<IGovernedCache>();
        var backend = first.GetRequiredService<IDistributedCache>();
        var policy = new CachePolicy(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2));
        var key = cache.CreateKey<RedisPayload>("redis.v1", "tenant", "item", policy);
        var otherKey = other.CreateKey<RedisPayload>("redis.v1", "tenant", "item", policy);
        var typeKey = other.CreateKey<string>("redis.v1", "tenant", "item", policy);
        var scopeKey = other.CreateKey<RedisPayload>("redis.v1", "another-tenant", "item", policy);
        var attemptedKeys = new HashSet<string>(StringComparer.Ordinal);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = timeout.Token;
        var value = new RedisPayload("中文 <tag>", 0);
        var unexpectedLoads = 0;
        var primaryFailure = false;
        try
        {
            attemptedKeys.Add(key.Value);
            await cache.SetAsync(key, value, token);
            await WaitForWrite(backend, key.Value, token);
            Assert.Equal(value, await other.GetOrCreateAsync(otherKey, _ =>
            {
                unexpectedLoads++;
                return ValueTask.FromResult(new RedisPayload("unexpected", -1));
            }, token));
            Assert.Equal(0, unexpectedLoads);

            await cache.RemoveAsync(key, token);
            Assert.Null(await backend.GetAsync(key.Value, token));
            Assert.Equal(value, await other.GetOrCreateAsync(otherKey, _ =>
            {
                unexpectedLoads++;
                return ValueTask.FromResult(new RedisPayload("unexpected", -1));
            }, token));
            Assert.Equal(0, unexpectedLoads);

            await Task.Delay(policy.LocalExpiration + TimeSpan.FromMilliseconds(100), token);
            var updated = new RedisPayload("updated", 1);
            Assert.Equal(updated, await other.GetOrCreateAsync(otherKey, _ =>
            {
                unexpectedLoads++;
                return ValueTask.FromResult(updated);
            }, token));
            Assert.Equal(1, unexpectedLoads);
            attemptedKeys.Add(typeKey.Value);
            Assert.Equal("different type", await other.GetOrCreateAsync(typeKey,
                _ => ValueTask.FromResult("different type"), token));
            attemptedKeys.Add(scopeKey.Value);
            Assert.Equal(new RedisPayload("another scope", 2), await other.GetOrCreateAsync(scopeKey,
                _ => ValueTask.FromResult(new RedisPayload("another scope", 2)), token));
            foreach (var ownKey in attemptedKeys) await WaitForWrite(backend, ownKey, token);

            first.Dispose();
            backend = second.GetRequiredService<IDistributedCache>();
            Assert.NotNull(await backend.GetAsync(key.Value, token));
        }
        catch
        {
            primaryFailure = true;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try
            {
                foreach (var ownKey in attemptedKeys) await backend.RemoveAsync(ownKey, cleanup.Token);
            }
            catch (Exception exception) when (primaryFailure)
            {
                output.WriteLine("Redis cleanup also failed ({0}); attempted keys expire after 30 seconds.",
                    exception.GetType().Name);
            }
        }
    }

    private static ServiceProvider Services(string connection, string prefix)
    {
        var services = new ServiceCollection();
        services.AddGovernedRedisCaching(options =>
        {
            var configuration = ConfigurationOptions.Parse(connection);
            options.ConfigurationOptions = configuration;
            options.InstanceName = prefix;
        }, serialization: builder => builder.AddGovernedJson(RedisTestJsonContext.Default.RedisPayload));
        return services.BuildServiceProvider();
    }

    private static async Task WaitForWrite(IDistributedCache backend, string key, CancellationToken token)
    {
        var deadline = Stopwatch.GetTimestamp() + 3 * Stopwatch.Frequency;
        do
        {
            if (await backend.GetAsync(key, token) is not null) return;
            await Task.Delay(25, token);
        } while (Stopwatch.GetTimestamp() < deadline);
        Assert.Fail("Redis did not contain the expected test key before the write deadline.");
    }
}

public sealed class RedisFactAttribute : FactAttribute
{
    public RedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MS_CACHE_REDIS_TEST_CONNECTION")))
            Skip = "Set MS_CACHE_REDIS_TEST_CONNECTION to run against a real Redis instance.";
    }
}

public sealed record RedisPayload(string Name, int Count);

[JsonSerializable(typeof(RedisPayload))]
internal partial class RedisTestJsonContext : JsonSerializerContext;
