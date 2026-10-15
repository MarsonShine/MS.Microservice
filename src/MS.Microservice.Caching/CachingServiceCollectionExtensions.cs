using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace MS.Microservice.Caching;

public static class CachingServiceCollectionExtensions
{
    public static IServiceCollection AddGovernedCaching(this IServiceCollection services,
        Action<CachingOptions>? configure = null, Action<IHybridCacheBuilder>? serialization = null)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IGovernedCache)))
            throw new InvalidOperationException("Register governed caching once per host.");
        var options = new CachingOptions(); configure?.Invoke(options);
        // Construct once to validate even disabled registrations, before any request uses the service.
        using (new GovernedCache(null, new CachingOptions { Enabled = false, ApplicationName = options.ApplicationName,
            ExpirationSeconds = options.ExpirationSeconds, LocalExpirationSeconds = options.LocalExpirationSeconds,
            MaximumConcurrentLoads = options.MaximumConcurrentLoads, MaximumPayloadBytes = options.MaximumPayloadBytes })) { }
        if (options.Enabled)
        {
            var builder = services.AddHybridCache(hybrid =>
            {
                hybrid.MaximumPayloadBytes = options.MaximumPayloadBytes;
                hybrid.MaximumKeyLength = 256;
            });
            serialization?.Invoke(builder);
        }
        services.AddSingleton<IGovernedCache>(provider => new GovernedCache(
            options.Enabled ? provider.GetRequiredService<HybridCache>() : null, options));
        return services;
    }

    /// <summary>Uses caller-provided JSON metadata for this cached type, including source-generated metadata.</summary>
    public static IHybridCacheBuilder AddGovernedJson<T>(this IHybridCacheBuilder builder, JsonTypeInfo<T> typeInfo)
        => builder.AddSerializer(new MetadataSerializer<T>(typeInfo));

    private sealed class MetadataSerializer<T>(JsonTypeInfo<T> typeInfo) : IHybridCacheSerializer<T>
    {
        public T Deserialize(ReadOnlySequence<byte> source)
        { var reader = new Utf8JsonReader(source); return JsonSerializer.Deserialize(ref reader, typeInfo)!; }
        public void Serialize(T value, IBufferWriter<byte> target)
        { using var writer = new Utf8JsonWriter(target); JsonSerializer.Serialize(writer, value, typeInfo); }
    }
}
