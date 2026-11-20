using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace MS.Microservice.Caching.Redis;

public static class RedisCachingServiceCollectionExtensions
{
    /// <summary>Registers governed caching with a Redis L2 whose connection is owned by the native provider.</summary>
    /// <remarks>Register once. Disabled caching skips Redis configuration; externally owned connection factories are unsupported.</remarks>
    public static IServiceCollection AddGovernedRedisCaching(this IServiceCollection services,
        Action<RedisCacheOptions> configureRedis, Action<CachingOptions>? configureCaching = null,
        Action<IHybridCacheBuilder>? serialization = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureRedis);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IGovernedCache)))
            throw new InvalidOperationException("Register governed caching once per host.");

        var caching = new CachingOptions();
        configureCaching?.Invoke(caching);
        if (!caching.Enabled)
            return services.AddGovernedCaching(options => Copy(caching, options), serialization);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IDistributedCache)))
            throw new InvalidOperationException("A distributed cache is already registered. Use AddGovernedCaching with that backend, or register this Redis adapter once.");

        var redis = new RedisCacheOptions { InstanceName = "cache:" };
        configureRedis(redis);
        if (redis.ConnectionMultiplexerFactory is not null)
            throw new ArgumentException("RedisCache owns and disposes its connection. Configure endpoints instead of a connection factory.", nameof(configureRedis));
        if (string.IsNullOrWhiteSpace(redis.InstanceName) || redis.InstanceName.Length > 128)
            throw new ArgumentException("Redis InstanceName must contain 1 to 128 characters.", nameof(configureRedis));

        ConfigurationOptions connection;
        try
        {
            connection = redis.ConfigurationOptions?.Clone()
                ?? ConfigurationOptions.Parse(redis.Configuration ?? string.Empty);
            if (connection.EndPoints.Count == 0)
                throw new ArgumentException();
        }
        catch (ArgumentException)
        {
            // Native parse errors can contain configuration values; do not attach them as inner exceptions.
            throw new ArgumentException("Redis requires valid StackExchange.Redis configuration with at least one endpoint.", nameof(configureRedis));
        }

        var instanceName = redis.InstanceName;
        var profiling = redis.ProfilingSession;
        services.AddGovernedCaching(options => Copy(caching, options), serialization);
        return services.AddStackExchangeRedisCache(options =>
        {
            options.ConfigurationOptions = connection.Clone();
            options.InstanceName = instanceName;
            options.ProfilingSession = profiling;
        });
    }

    private static void Copy(CachingOptions source, CachingOptions target)
    {
        target.Enabled = source.Enabled;
        target.ApplicationName = source.ApplicationName;
        target.ExpirationSeconds = source.ExpirationSeconds;
        target.LocalExpirationSeconds = source.LocalExpirationSeconds;
        target.MaximumConcurrentLoads = source.MaximumConcurrentLoads;
        target.MaximumPayloadBytes = source.MaximumPayloadBytes;
    }
}
