namespace MS.Microservice.Core.Concurrent;

/// <summary>Compatibility entry point for the shared Threading Singleflight implementation.</summary>
public class SingleflightManager
{
    private readonly MS.Microservice.Threading.SingleflightManager _inner = new();

    public Task<T> ExecuteOnceAsync<T>(string key, Func<Task<T>> action) => _inner.ExecuteOnceAsync(key, action);
}
