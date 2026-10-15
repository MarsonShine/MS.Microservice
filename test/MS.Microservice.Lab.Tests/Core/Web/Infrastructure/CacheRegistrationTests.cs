using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
}
