using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MS.Microservice.Caching;
using MS.Microservice.Caching.Redis;
using StackExchange.Redis;
using Xunit;

public sealed class RedisRegistrationTests
{
    [Fact]
    public async Task DisabledCachingSkipsRedisConfigurationAndSourceValuesRemainLive()
    {
        var services = new ServiceCollection();
        services.AddGovernedRedisCaching(_ => throw new InvalidOperationException("Must not be called."),
            options => options.Enabled = false);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IDistributedCache));
        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IGovernedCache>();
        var key = cache.CreateKey<int>("disabled", "tenant", "item");
        var loads = 0;
        Assert.Equal(1, await cache.GetOrCreateAsync(key, _ => ValueTask.FromResult(++loads)));
        Assert.Equal(2, await cache.GetOrCreateAsync(key, _ => ValueTask.FromResult(++loads)));
    }

    [Fact]
    public void RegistrationUsesNativeProviderAndInvokesConfigurationOnceWithoutConnecting()
    {
        var redisCalls = 0;
        var cacheCalls = 0;
        var serializerCalls = 0;
        var services = new ServiceCollection();
        services.AddGovernedRedisCaching(options =>
        {
            redisCalls++;
            options.Configuration = "localhost:6379";
        }, options =>
        {
            cacheCalls++;
            options.ApplicationName = "test.redis";
            options.ExpirationSeconds = 20;
            options.LocalExpirationSeconds = 3;
        }, _ => serializerCalls++);
        using var provider = services.BuildServiceProvider();
        Assert.IsAssignableFrom<RedisCache>(provider.GetRequiredService<IDistributedCache>());
        Assert.Equal("cache:", provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value.InstanceName);
        var key = provider.GetRequiredService<IGovernedCache>().CreateKey<int>("settings", "tenant", "item");
        Assert.StartsWith("test.redis/", key.Value);
        Assert.Equal(TimeSpan.FromSeconds(20), key.Policy.Expiration);
        Assert.Equal(TimeSpan.FromSeconds(3), key.Policy.LocalExpiration);
        Assert.Equal(1, redisCalls);
        Assert.Equal(1, cacheCalls);
        Assert.Equal(1, serializerCalls);
    }

    [Fact]
    public void ExistingGovernedOrDistributedRegistrationIsRejected()
    {
        var governedServices = new ServiceCollection();
        governedServices.AddGovernedCaching();
        Assert.Throws<InvalidOperationException>(() => governedServices.AddGovernedRedisCaching(Configure));
        var distributedServices = new ServiceCollection();
        distributedServices.AddSingleton<IDistributedCache, UnusedBackend>();
        Assert.Throws<InvalidOperationException>(() => distributedServices.AddGovernedRedisCaching(Configure));
    }

    [Fact]
    public void MissingEndpointsAreRejectedBeforeProviderRegistration()
    {
        var services = new ServiceCollection();
        Assert.ThrowsAny<ArgumentException>(() => services.AddGovernedRedisCaching(_ => { }));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IDistributedCache));
        Assert.ThrowsAny<ArgumentException>(() => services.AddGovernedRedisCaching(options =>
            options.ConfigurationOptions = new ConfigurationOptions()));
    }

    [Theory]
    [InlineData("unrecognized-option=true")]
    [InlineData("poolsize=200")]
    [InlineData("prefix=another-client-prefix")]
    [InlineData("ssl=not-a-boolean")]
    [InlineData("connectTimeout=not-a-number")]
    public void InvalidConnectionDoesNotEchoCredentials(string invalidSetting)
    {
        const string secret = "do-not-echo-redis-password";
        var services = new ServiceCollection();
        var exception = Assert.ThrowsAny<ArgumentException>(() => services.AddGovernedRedisCaching(options =>
            options.Configuration = $"localhost:6379,password={secret},{invalidSetting}"));
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NativeConfigurationObjectTakesPrecedenceAndIsSnapshotted()
    {
        var configuration = ConfigurationOptions.Parse("localhost:6380");
        var services = new ServiceCollection();
        services.AddGovernedRedisCaching(options =>
        {
            options.Configuration = "invalid-setting=true";
            options.ConfigurationOptions = configuration;
            options.InstanceName = new string('p', 128);
        });
        configuration.EndPoints.Clear();
        using var provider = services.BuildServiceProvider();
        var registered = provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value;
        Assert.Single(registered.ConfigurationOptions!.EndPoints);
        Assert.Equal(128, registered.InstanceName!.Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidGovernanceIsRejectedWithoutRegisteringRedis(bool enabled)
    {
        var services = new ServiceCollection();
        Assert.ThrowsAny<ArgumentException>(() => services.AddGovernedRedisCaching(Configure, options =>
        {
            options.Enabled = enabled;
            options.LocalExpirationSeconds = options.ExpirationSeconds + 1;
        }));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IDistributedCache));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void EmptyNamespaceIsRejected(string? instanceName)
        => Assert.ThrowsAny<ArgumentException>(() => new ServiceCollection().AddGovernedRedisCaching(options =>
        {
            Configure(options);
            options.InstanceName = instanceName;
        }));

    [Fact]
    public void NamespaceLimitAndExternalConnectionOwnershipAreValidated()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ServiceCollection().AddGovernedRedisCaching(options =>
        {
            Configure(options);
            options.InstanceName = new string('x', 129);
        }));
        Assert.ThrowsAny<ArgumentException>(() => new ServiceCollection().AddGovernedRedisCaching(options =>
        {
            Configure(options);
            options.ConnectionMultiplexerFactory = () => throw new NotSupportedException();
        }));
    }

    private static void Configure(RedisCacheOptions options) => options.Configuration = "localhost:6379";

    private sealed class UnusedBackend : IDistributedCache
    {
        public byte[]? Get(string key) => throw new NotSupportedException();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new NotSupportedException();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
            => throw new NotSupportedException();
        public void Remove(string key) => throw new NotSupportedException();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
        public void Refresh(string key) => throw new NotSupportedException();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
    }
}
