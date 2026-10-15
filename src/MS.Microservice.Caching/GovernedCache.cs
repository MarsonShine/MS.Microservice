using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Hybrid;

namespace MS.Microservice.Caching;

public interface IGovernedCache
{
    /// <summary>Creates a typed, application- and scope-isolated key. Use a fixed, versioned region.</summary>
    CacheKey<T> CreateKey<T>(string region, string scope, string resourceId, CachePolicy? policy = null);
    /// <summary>Reads or loads a value with the key's policy. Loaders must use their supplied token and own dependency lifetimes.</summary>
    ValueTask<T> GetOrCreateAsync<T, TState>(CacheKey<T> key, TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory, CancellationToken cancellationToken = default);
    /// <summary>Stores a value using the key's policy; disabled or bypassed caching performs no write.</summary>
    ValueTask SetAsync<T>(CacheKey<T> key, T value, CancellationToken cancellationToken = default);
    /// <summary>Invalidates this instance and its shared backend; other instances' local copies may remain until expiration.</summary>
    ValueTask RemoveAsync<T>(CacheKey<T> key, CancellationToken cancellationToken = default);
}

public static class GovernedCacheExtensions
{
    public static ValueTask<T> GetOrCreateAsync<T>(this IGovernedCache cache, CacheKey<T> key,
        Func<CancellationToken, ValueTask<T>> factory, CancellationToken cancellationToken = default)
        => cache.GetOrCreateAsync(key, factory, static (load, token) => load(token), cancellationToken);
}

/// <summary>Scoped typed keys and bounded source loading over the host's HybridCache.</summary>
public sealed class GovernedCache : IGovernedCache, IDisposable
{
    public const string MeterName = "MS.Microservice.Caching";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Loads = Meter.CreateCounter<long>("cache.source.loads");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("cache.requests");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("cache.source.rejected");
    private static readonly Histogram<double> LoadDuration = Meter.CreateHistogram<double>("cache.source.duration", "ms");
    private readonly HybridCache? _backend;
    private readonly string _application;
    private readonly CachePolicy _defaultPolicy;
    private readonly SemaphoreSlim _loadBudget;

    public GovernedCache(HybridCache? backend, CachingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateName(options.ApplicationName, nameof(options.ApplicationName));
        _application = options.ApplicationName;
        _defaultPolicy = new(TimeSpan.FromSeconds(options.ExpirationSeconds), TimeSpan.FromSeconds(options.LocalExpirationSeconds));
        if (options.MaximumConcurrentLoads is < 1 or > 10000 || options.MaximumPayloadBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        _loadBudget = new(options.MaximumConcurrentLoads);
        _backend = options.Enabled ? backend ?? throw new ArgumentNullException(nameof(backend)) : null;
    }

    public CacheKey<T> CreateKey<T>(string region, string scope, string resourceId, CachePolicy? policy = null)
    {
        ValidateName(region, nameof(region));
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        if (scope.Length > 256 || resourceId.Length > 512) throw new ArgumentOutOfRangeException(nameof(resourceId));
        var type = typeof(T).FullName!;
        var identity = $"{type.Length}:{type}{scope.Length}:{scope}{resourceId.Length}:{resourceId}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(identity)));
        return new(_application, $"{_application}/{region}/{hash}", region, policy ?? _defaultPolicy);
    }

    public ValueTask<T> GetOrCreateAsync<T, TState>(CacheKey<T> key, TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory, CancellationToken cancellationToken = default)
    {
        CheckKey(key); ArgumentNullException.ThrowIfNull(factory); cancellationToken.ThrowIfCancellationRequested();
        if (_backend is null || key.Policy.IsBypassed) return factory(state, cancellationToken);
        Requests.Add(1, new KeyValuePair<string, object?>("region", key.Region));
        var load = new LoadState<T, TState>(this, key.Region, state, factory);
        return _backend.GetOrCreateAsync(key.Value, load, static (value, token) => Load(value, token), key.Policy.Options,
            cancellationToken: cancellationToken);
    }
    public ValueTask SetAsync<T>(CacheKey<T> key, T value, CancellationToken cancellationToken = default)
    {
        CheckKey(key); cancellationToken.ThrowIfCancellationRequested();
        return _backend is null || key.Policy.IsBypassed ? ValueTask.CompletedTask
            : _backend.SetAsync(key.Value, value, key.Policy.Options, cancellationToken: cancellationToken);
    }
    public ValueTask RemoveAsync<T>(CacheKey<T> key, CancellationToken cancellationToken = default)
    {
        CheckKey(key); cancellationToken.ThrowIfCancellationRequested();
        return _backend?.RemoveAsync(key.Value, cancellationToken) ?? ValueTask.CompletedTask;
    }
    private static async ValueTask<T> Load<T, TState>(LoadState<T, TState> load, CancellationToken token)
    {
        var owner = load.Owner;
        if (!owner._loadBudget.Wait(0, token))
        {
            Rejected.Add(1, new KeyValuePair<string, object?>("region", load.Region));
            throw new CacheLoadLimitException();
        }
        var started = Stopwatch.GetTimestamp();
        Loads.Add(1, new KeyValuePair<string, object?>("region", load.Region));
        try { return await load.Factory(load.State, token).ConfigureAwait(false); }
        finally
        {
            owner._loadBudget.Release();
            LoadDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, new KeyValuePair<string, object?>("region", load.Region));
        }
    }
    private void CheckKey<T>(CacheKey<T> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Owner != _application) throw new ArgumentException("Cache keys belong to their configured application.", nameof(key));
    }
    private static void ValidateName(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.Length > 64 || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_'))
            throw new ArgumentException("Use 1–64 ASCII letters/digits/dots/hyphens/underscores.", parameter);
    }
    public void Dispose() => _loadBudget.Dispose();
    private readonly record struct LoadState<T, TState>(GovernedCache Owner, string Region, TState State,
        Func<TState, CancellationToken, ValueTask<T>> Factory);
}

/// <summary>The source-load budget is exhausted; hosts may apply backpressure instead of unbounded fallback.</summary>
public sealed class CacheLoadLimitException() : InvalidOperationException("The concurrent cache source-load budget is exhausted.");
