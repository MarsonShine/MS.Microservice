using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Caching;

const string application = "bench";
const string region = "orders.v1";
const string shortScope = "tenant";
const string shortId = "item-42";
const int expected = 42;
var verifyOnly = args is ["--verify-only"];
var services = new ServiceCollection();
services.AddGovernedCaching(options =>
{
    options.ApplicationName = application;
    options.LocalExpirationSeconds = 3600;
    options.ExpirationSeconds = 3600;
});
using var provider = services.BuildServiceProvider();
var cache = provider.GetRequiredService<IGovernedCache>();
var longScope = string.Concat(Enumerable.Repeat("租🚀", 85)) + "甲";
var longId = string.Concat(Enumerable.Repeat("货📦", 170)) + "甲乙";

var prebuilt = CheckKey<int>(shortScope, shortId);
CheckKey<int>(longScope, longId);
CheckKey<CachedPage<OrderDto>>(shortScope, shortId);
await cache.SetAsync(prebuilt, expected);

Measure("CreateKey short ASCII", 20_000, () => cache.CreateKey<int>(region, shortScope, shortId));
Measure("CreateKey Unicode (256/512 UTF-16 units)", 5_000, () => cache.CreateKey<int>(region, longScope, longId));
Measure("CreateKey generic DTO", 20_000, () => cache.CreateKey<CachedPage<OrderDto>>(region, shortScope, shortId));
Measure("L1 hit, prebuilt key", 20_000, () => Read(prebuilt));
Measure("L1 hit, create key per call", 20_000, () => Read(cache.CreateKey<int>(region, shortScope, shortId)));
Console.WriteLine("Verified complete legacy keys and L1 hit values.");

CacheKey<T> CheckKey<T>(string scope, string resourceId)
{
    var type = typeof(T).FullName!;
    var framed = $"{type.Length}:{type}{scope.Length}:{scope}{resourceId.Length}:{resourceId}";
    var utf8 = new UTF8Encoding(false, true);
    var digest = Convert.ToHexStringLower(SHA256.HashData(utf8.GetBytes(framed)));
    var key = cache.CreateKey<T>(region, scope, resourceId);
    if (key.Value != $"{application}/{region}/{digest}" || key.Region != region)
    {
        throw new InvalidOperationException($"Key formula mismatch for {type}.");
    }
    return key;
}

int Read(CacheKey<int> key)
{
    var hit = cache.GetOrCreateAsync(key, static _ =>
        throw new InvalidOperationException("Unexpected source load during an L1 hit."));
    if (!hit.IsCompleted || hit.Result != expected)
    {
        throw new InvalidOperationException("Expected an immediate L1 hit with the seeded value.");
    }
    return expected;
}

void Measure<T>(string name, int iterations, Func<T> run)
{
    T last = run();
    if (verifyOnly)
    {
        return;
    }
    const int rounds = 7;
    for (var i = 0; i < iterations / 5; i++)
    {
        last = run();
    }
    var times = new double[rounds];
    var allocations = new double[rounds];
    for (var round = 0; round < rounds; round++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            last = run();
        }
        times[round] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
        allocations[round] = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
        GC.KeepAlive(last);
    }
    Array.Sort(times);
    Array.Sort(allocations);
    Console.WriteLine($"{name}: {times[rounds / 2]:F1} ns/op, {allocations[rounds / 2]:F1} B/op");
}

internal sealed record OrderDto(int Id);
internal sealed record CachedPage<T>(T[] Items);
