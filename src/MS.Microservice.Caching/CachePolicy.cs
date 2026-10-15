using Microsoft.Extensions.Caching.Hybrid;

namespace MS.Microservice.Caching;

/// <summary>Immutable absolute expiration policy; null and ordinary results share the same TTL.</summary>
public sealed class CachePolicy
{
    public static CachePolicy Bypass { get; } = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), bypass: true);
    public CachePolicy(TimeSpan expiration, TimeSpan localExpiration, bool bypass = false)
    {
        if (expiration <= TimeSpan.Zero || localExpiration <= TimeSpan.Zero || localExpiration > expiration)
            throw new ArgumentOutOfRangeException(nameof(expiration), "Require 0 < local expiration <= shared expiration.");
        Options = new() { Expiration = expiration, LocalCacheExpiration = localExpiration };
        IsBypassed = bypass;
    }
    public TimeSpan Expiration => Options.Expiration!.Value;
    public TimeSpan LocalExpiration => Options.LocalCacheExpiration!.Value;
    public bool IsBypassed { get; }
    internal HybridCacheEntryOptions Options { get; }
}

public sealed class CachingOptions
{
    public const string SectionName = "Caching";
    public bool Enabled { get; set; } = true;
    public string ApplicationName { get; set; } = "MS.Microservice";
    public int ExpirationSeconds { get; set; } = 300;
    public int LocalExpirationSeconds { get; set; } = 30;
    public int MaximumConcurrentLoads { get; set; } = 32;
    public int MaximumPayloadBytes { get; set; } = 1024 * 1024;
}

/// <summary>A prebuilt typed key; reuse it on hot paths rather than rebuilding identity hashes.</summary>
public sealed class CacheKey<T>
{
    internal CacheKey(string owner, string value, string region, CachePolicy policy)
    { Owner = owner; Value = value; Region = region; Policy = policy; }
    public string Value { get; }
    public string Region { get; }
    public CachePolicy Policy { get; }
    internal string Owner { get; }
}
