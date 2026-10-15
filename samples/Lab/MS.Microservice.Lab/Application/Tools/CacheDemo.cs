using System.Collections.Concurrent;

namespace MS.Microservice.Lab.Application.Tools;

internal sealed class CacheDemo
{
    private readonly ConcurrentDictionary<string, string> _source = new(StringComparer.Ordinal);
    private long _reads;
    internal long Reads => Interlocked.Read(ref _reads);
    internal ValueTask<string> Read(string scope, CancellationToken token)
    { token.ThrowIfCancellationRequested(); Interlocked.Increment(ref _reads); return ValueTask.FromResult(_source.GetOrAdd(scope, "初始示例值")); }
    internal void Write(string scope, string value) => _source[scope] = value;
}
