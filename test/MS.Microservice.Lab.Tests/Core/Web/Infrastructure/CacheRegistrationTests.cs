using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MS.Microservice.Caching;
using MS.Microservice.Lab.Infrastructure.Extensions;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public sealed class CacheRegistrationTests
{
    [Fact]
    public async Task HostUsesGovernedCachingWithConfiguredIdentityAndExpiration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Caching:ApplicationName"] = "test", ["Caching:ExpirationSeconds"] = "300", ["Caching:LocalExpirationSeconds"] = "60"
        }).Build();
        var services = new ServiceCollection(); services.AddApplicationCaching(configuration);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IDistributedCache));
        using var provider = services.BuildServiceProvider(); var cache = provider.GetRequiredService<IGovernedCache>();
        var key = cache.CreateKey<int>("sample.v1", "scope", "id");
        Assert.StartsWith("test/", key.Value); Assert.Equal(TimeSpan.FromSeconds(60), key.Policy.LocalExpiration);
        var calls = 0;
        await cache.GetOrCreateAsync(key, _ => ValueTask.FromResult(++calls));
        await cache.GetOrCreateAsync(key, _ => ValueTask.FromResult(++calls)); Assert.Equal(1, calls);
        await cache.RemoveAsync(key); await cache.GetOrCreateAsync(key, _ => ValueTask.FromResult(++calls)); Assert.Equal(2, calls);
    }
    [Theory]
    [InlineData("Caching:ExpirationSeconds", "0")]
    [InlineData("Caching:LocalExpirationSeconds", "0")]
    [InlineData("Caching:LocalExpirationSeconds", "999")]
    public void InvalidExpirationFailsAtRegistration(string name, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [name] = value }).Build();
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddApplicationCaching(configuration));
    }

    [Theory]
    [InlineData(null, "MS.Microservice.Lab:")]
    [InlineData("test-cache:", "test-cache:")]
    public void RedisBackendRegistersNativeProviderWithoutConnecting(string? configuredPrefix, string expectedPrefix)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Caching:Backend"] = "Redis",
            ["ConnectionStrings:CacheRedis"] = "localhost:6379,ssl=false",
            ["Caching:Redis:InstanceName"] = configuredPrefix
        }).Build();
        var services = new ServiceCollection();
        services.AddApplicationCaching(configuration);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IDistributedCache));
        using var provider = services.BuildServiceProvider();
        Assert.IsAssignableFrom<RedisCache>(provider.GetRequiredService<IDistributedCache>());
        var options = provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value;
        Assert.Equal(expectedPrefix, options.InstanceName);
        Assert.Single(options.ConfigurationOptions!.EndPoints);
    }

    [Theory]
    [InlineData("Redis")]
    [InlineData("Unknown")]
    public async Task DisabledCachingSkipsBackendRequirements(string backend)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Caching:Enabled"] = "false", ["Caching:Backend"] = backend
        }).Build();
        var services = new ServiceCollection();
        services.AddApplicationCaching(configuration);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IDistributedCache));
        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IGovernedCache>();
        var key = cache.CreateKey<int>("sample.v1", "scope", "id");
        var calls = 0;
        await cache.GetOrCreateAsync(key, _ => ValueTask.FromResult(++calls));
        await cache.GetOrCreateAsync(key, _ => ValueTask.FromResult(++calls));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("Unknown", "Caching:Backend")]
    [InlineData("Redis", "ConnectionStrings:CacheRedis")]
    public void EnabledBackendRejectsInvalidSelectionOrMissingConnection(string backend, string expectedSetting)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Caching:Backend"] = backend
        }).Build();
        var services = new ServiceCollection();
        var error = Assert.Throws<InvalidOperationException>(() => services.AddApplicationCaching(configuration));
        Assert.Contains(expectedSetting, error.Message);
        Assert.Empty(services);
    }
}
