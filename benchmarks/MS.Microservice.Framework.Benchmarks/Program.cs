using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MS.Microservice.Caching;
using MS.Microservice.Core.Security.SensitiveData;

var services = new ServiceCollection(); services.AddGovernedCaching();
using var provider = services.BuildServiceProvider();
var cache = provider.GetRequiredService<IGovernedCache>();
var native = provider.GetRequiredService<HybridCache>();
var key = cache.CreateKey<string>("bench.v1", "owner", "sample");
var nativeOptions = new HybridCacheEntryOptions { Expiration = key.Policy.Expiration, LocalCacheExpiration = key.Policy.LocalExpiration };
const string payload = "cached immutable payload";
static ValueTask<string> Load(string value, CancellationToken token) => ValueTask.FromResult(value);
cache.GetOrCreateAsync(key, payload, Load).GetAwaiter().GetResult();
var bypass = cache.CreateKey<string>("bench.v1", "owner", "bypass", CachePolicy.Bypass);
var input = new BenchInput();
var coldStarted = Stopwatch.GetTimestamp(); SensitiveValues.Project(input);
var coldProjectionMs = Stopwatch.GetElapsedTime(coldStarted).TotalMilliseconds;
var plainJson = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
var safeJson = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }; safeJson.UseSensitiveValues();
var projectedInput = new BenchInput { Phone = SensitiveValues.Protect(input.Phone, SensitiveKind.Phone)!,
    Email = SensitiveValues.Protect(input.Email, SensitiveKind.Email)!, InternalNote = null };
if (JsonSerializer.Serialize(projectedInput, plainJson) != JsonSerializer.Serialize(input, safeJson))
    throw new InvalidOperationException("JSON benchmark outputs must match.");
var enabledLog = new BenchLogger(true); var disabledLog = new BenchLogger(false);
const int rounds = 7, iterations = 100000, sampleStride = 128;
var cases = new (string Name, Func<int> Run)[]
{
    ("cache.native.hit", () => native.GetOrCreateAsync(key.Value, payload, Load, nativeOptions).GetAwaiter().GetResult().Length),
    ("cache.governed.hit", () => cache.GetOrCreateAsync(key, payload, Load).GetAwaiter().GetResult().Length),
    ("cache.native.miss", () => { native.RemoveAsync(key.Value).GetAwaiter().GetResult(); return native.GetOrCreateAsync(key.Value, payload, Load, nativeOptions).GetAwaiter().GetResult().Length; }),
    ("cache.governed.miss", () => { cache.RemoveAsync(key).GetAwaiter().GetResult(); return cache.GetOrCreateAsync(key, payload, Load).GetAwaiter().GetResult().Length; }),
    ("cache.bypass", () => cache.GetOrCreateAsync(bypass, payload, Load).GetAwaiter().GetResult().Length),
    ("cache.key.create", () => cache.CreateKey<string>("bench.v1", "owner", "sample").Value.Length),
    ("sensitive.phone", () => SensitiveValues.Protect(input.Phone, SensitiveKind.Phone)!.Length),
    ("sensitive.email", () => SensitiveValues.Protect(input.Email, SensitiveKind.Email)!.Length),
    ("projection.manual", () => new Dictionary<string, object?>(4, StringComparer.Ordinal)
        { ["Name"] = input.Name, ["Phone"] = SensitiveValues.Protect(input.Phone, SensitiveKind.Phone),
          ["Email"] = SensitiveValues.Protect(input.Email, SensitiveKind.Email), ["Count"] = input.Count }.Count),
    ("projection.attributes", () => SensitiveValues.Project(input).Count),
    ("json.preprotected", () => JsonSerializer.Serialize(projectedInput, plainJson).Length),
    ("json.attributes", () => JsonSerializer.Serialize(input, safeJson).Length),
    ("log.plain.disabled", () => { disabledLog.LogInformation("Phone={Phone}", input.Phone); return 1; }),
    ("log.sensitive.disabled", () => { disabledLog.LogSensitive(LogLevel.Information, "sample", input); return 1; }),
    ("log.sensitive.enabled", () => { enabledLog.LogSensitive(LogLevel.Information, "sample", input); return 1; })
};
var totals = cases.ToDictionary(item => item.Name, _ => new Totals());
var checksum = 0;
foreach (var item in cases) for (var warmup = 0; warmup < 5000; warmup++) checksum = unchecked(checksum + item.Run());
for (var round = 0; round < rounds; round++)
for (var offset = 0; offset < cases.Length; offset++)
{
    var item = cases[(offset + round) % cases.Length];
    var samples = new List<double>(iterations / sampleStride + 1);
    var before = GC.GetAllocatedBytesForCurrentThread();
    var started = Stopwatch.GetTimestamp();
    for (var iteration = 0; iteration < iterations; iteration++)
    {
        if (iteration % sampleStride == 0)
        {
            var sampleStarted = Stopwatch.GetTimestamp();
            checksum = unchecked(checksum + item.Run());
            samples.Add(Stopwatch.GetElapsedTime(sampleStarted).TotalNanoseconds);
        }
        else checksum = unchecked(checksum + item.Run());
    }
    var elapsedNs = Stopwatch.GetElapsedTime(started).TotalNanoseconds;
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    var aggregate = totals[item.Name];
    aggregate.Ns.Add(elapsedNs / iterations); aggregate.Bytes.Add((double)allocated / iterations); aggregate.Samples.AddRange(samples);
}
var results = totals.Select(pair =>
{
    var ns = pair.Value.Ns.Order().ToArray(); var bytes = pair.Value.Bytes.Order().ToArray(); var sampled = pair.Value.Samples.Order().ToArray();
    return new Result(pair.Key, ns[rounds / 2], bytes[rounds / 2], sampled[(int)Math.Ceiling(sampled.Length * .95) - 1]);
}).ToList();
// Observe the overhead of active synchronous Meter callbacks separately from exporters.
using (var listener = new MeterListener())
{
    listener.InstrumentPublished = (instrument, owner) => { if (instrument.Meter.Name == GovernedCache.MeterName) owner.EnableMeasurementEvents(instrument); };
    listener.SetMeasurementEventCallback<long>(static (_, _, _, _) => { });
    listener.SetMeasurementEventCallback<double>(static (_, _, _, _) => { });
    listener.Start();
    var ns = new List<double>(); var allocations = new List<double>();
    for (var round = 0; round < rounds; round++)
    {
        var before = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
        for (var iteration = 0; iteration < iterations; iteration++) checksum = unchecked(checksum + cache.GetOrCreateAsync(key, payload, Load).GetAwaiter().GetResult().Length);
        ns.Add(Stopwatch.GetElapsedTime(started).TotalNanoseconds / iterations); allocations.Add((double)(GC.GetAllocatedBytesForCurrentThread() - before) / iterations);
    }
    results.Add(new("cache.governed.hit.listener", ns.Order().ElementAt(rounds / 2), allocations.Order().ElementAt(rounds / 2), null));
}
var capture = AuditCaptureBenchmark.Run();
var report = new
{
    MeasuredAtUtc = DateTimeOffset.UtcNow, Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
    ProcessorCount = Environment.ProcessorCount, StopwatchFrequency = Stopwatch.Frequency, Rounds = rounds, IterationsPerRound = iterations,
    SampleStride = sampleStride, ColdProjectionMs = coldProjectionMs, Results = results, AuditCapture = capture,
    Notes = "Single-thread warm microbenchmarks. Median batch ns/op and allocated bytes/op; sampled P95 includes timestamp overhead. Cache misses include removal. Listener is not an exporter. JSON output bytes match. Audit capture stops before SQL. No Redis, PostgreSQL, HTTP or sink I/O capacity claim."
};
var output = args.FirstOrDefault() ?? "artifacts/framework-component-performance.json";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
foreach (var row in results) Console.WriteLine($"{row.Scenario,-30} {row.MedianNs,10:F1} ns/op {row.MedianBytes,8:F1} B/op sampled P95={row.SampledP95Ns:F1}");
foreach (var row in capture) Console.WriteLine($"audit.capture.{row.Variant,-10} {row.MedianNs:F1} ns {row.MedianBytes:F1} B");
Console.WriteLine($"Cold projection metadata: {coldProjectionMs:F3}ms; checksum={checksum}; report={output}");

internal sealed class Totals
{
    internal List<double> Ns { get; } = []; internal List<double> Bytes { get; } = []; internal List<double> Samples { get; } = [];
}
internal sealed record Result(string Scenario, double MedianNs, double MedianBytes, double? SampledP95Ns);
public sealed class BenchInput
{
    public string Name { get; set; } = "Example";
    [Sensitive(SensitiveKind.Phone)] public string Phone { get; set; } = "13800138000";
    [Sensitive(SensitiveKind.Email)] public string Email { get; set; } = "alice@example.com";
    [Sensitive(SensitiveKind.Secret)] public string? InternalNote { get; set; } = "private-note";
    public int Count { get; set; } = 3;
}
internal sealed class BenchLogger(bool enabled) : ILogger
{
    public bool IsEnabled(LogLevel level) => enabled;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => GC.KeepAlive(state);
}
