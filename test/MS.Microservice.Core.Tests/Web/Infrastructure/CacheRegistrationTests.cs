using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.Core.Ceching;
using MS.Microservice.Web.Infrastructure.Extensions;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public sealed class CacheRegistrationTests
{
    [Fact]
    public async Task AddApplicationCaching_RegistersBothCacheContractsAndSupportsDistributedRoundTrip()
    {
        var configuration = CreateConfiguration(
            ("CacheOptions:KeyPrefix", "test."),
            ("CacheOptions:AbsoluteExpirationSecond", "300"),
            ("CacheOptions:SlidingExpirationSecond", "60"));
        var services = new ServiceCollection();

        services.AddApplicationCaching(configuration);

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var distributedCache = provider.GetRequiredService<IDistributedCache>();
        var hybridCache = provider.GetRequiredService<HybridCache>();
        var options = provider.GetRequiredService<IOptions<CacheOptions>>().Value;

        Assert.NotNull(hybridCache);
        Assert.Equal("test.", options.KeyPrefix);
        Assert.Equal(300, options.AbsoluteExpirationSecond);
        Assert.Equal(60, options.SlidingExpirationSecond);

        byte[] expected = [1, 2, 3];
        await distributedCache.SetAsync("cache-registration", expected);
        Assert.Equal(expected, await distributedCache.GetAsync("cache-registration"));

        await distributedCache.RemoveAsync("cache-registration");
        Assert.Null(await distributedCache.GetAsync("cache-registration"));
    }

    [Theory]
    [InlineData("CacheOptions:SlidingExpirationSecond", "0", "SlidingExpirationSecond")]
    [InlineData("CacheOptions:AbsoluteExpirationSecond", "0", "AbsoluteExpirationSecond")]
    public async Task AddApplicationCaching_WhenExpirationIsInvalid_FailsHostStartup(
        string invalidKey,
        string invalidValue,
        string expectedMember)
    {
        var values = new Dictionary<string, string?>
        {
            ["CacheOptions:SlidingExpirationSecond"] = "60",
            [invalidKey] = invalidValue
        };
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(values);
        builder.Logging.ClearProviders();
        builder.Services.AddApplicationCaching(builder.Configuration);

        using var host = builder.Build();
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync());
        Assert.Contains(expectedMember, exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration CreateConfiguration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
            .Build();
}
