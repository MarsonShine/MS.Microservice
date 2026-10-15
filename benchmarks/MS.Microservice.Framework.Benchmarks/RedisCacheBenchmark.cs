using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Caching;
using MS.Microservice.Caching.Redis;

internal static class RedisCacheBenchmark
{
    private const int L1Rounds = 5;
    private const int L1Iterations = 500_000;
    private const int L2Rounds = 3;
    private const int L2Iterations = 1000;
    private static int _checksum;

    internal static async Task<int> RunAsync(string? output)
    {
        var connection = Environment.GetEnvironmentVariable("MS_CACHE_REDIS_TEST_CONNECTION");
        var prefix = Environment.GetEnvironmentVariable("MS_CACHE_REDIS_TEST_PREFIX");
        if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(prefix) || prefix.Length > 95)
        {
            Console.Error.WriteLine("Redis benchmark requires MS_CACHE_REDIS_TEST_CONNECTION and a nonempty MS_CACHE_REDIS_TEST_PREFIX of at most 95 characters.");
            return 1;
        }

        // Reserve the last five seconds of the operation budget for exact-key cleanup.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        ServiceProvider? provider = null;
        IDistributedCache? distributed = null;
        string? ownedKey = null;
        var failed = false;
        var cleanupSucceeded = false;
        object? report = null;
        try
        {
            var services = new ServiceCollection();
            services.AddGovernedRedisCaching(redis =>
            {
                redis.Configuration = connection;
                redis.InstanceName = prefix + Guid.NewGuid().ToString("N") + ":";
            }, cache =>
            {
                cache.ApplicationName = "RedisBenchmark";
                cache.ExpirationSeconds = 600;
                cache.LocalExpirationSeconds = 300;
            });
            provider = services.BuildServiceProvider();
            var governed = provider.GetRequiredService<IGovernedCache>();
            var native = provider.GetRequiredService<HybridCache>();
            distributed = provider.GetRequiredService<IDistributedCache>();
            var key = governed.CreateKey<string>("redis-bench.v1", "benchmark", "payload");
            ownedKey = key.Value;
            var payload = new string('x', 1024);
            var options = new HybridCacheEntryOptions
            {
                Expiration = key.Policy.Expiration,
                LocalCacheExpiration = key.Policy.LocalExpiration
            };
            var l2Options = new HybridCacheEntryOptions
            {
                Expiration = key.Policy.Expiration,
                LocalCacheExpiration = key.Policy.LocalExpiration,
                Flags = HybridCacheEntryFlags.DisableLocalCacheRead | HybridCacheEntryFlags.DisableLocalCacheWrite
            };
            await native.SetAsync(key.Value, payload, options, cancellationToken: deadline.Token)
                .AsTask().WaitAsync(deadline.Token);
            var raw = await distributed.GetAsync(key.Value, deadline.Token).WaitAsync(deadline.Token);
            if (raw is not { Length: > 0 }) throw new InvalidOperationException("Redis benchmark setup did not persist its payload.");
            var restored = await native.GetOrCreateAsync(key.Value, payload, RequireCached, l2Options,
                cancellationToken: deadline.Token).AsTask().WaitAsync(deadline.Token);
            if (restored != payload) throw new InvalidOperationException("Redis benchmark setup returned a different payload.");

            static int NativeHit(HybridCache cache, string cacheKey, string expected, HybridCacheEntryOptions policy)
                => cache.GetOrCreateAsync(cacheKey, expected, RequireCached, policy).GetAwaiter().GetResult().Length;
            static int GovernedHit(IGovernedCache cache, CacheKey<string> cacheKey, string expected)
                => cache.GetOrCreateAsync(cacheKey, expected, RequireCached).GetAwaiter().GetResult().Length;
            Func<int> nativeHit = () => NativeHit(native, key.Value, payload, options);
            Func<int> governedHit = () => GovernedHit(governed, key, payload);
            for (var warmup = 0; warmup < 10_000; warmup++)
            {
                _checksum = unchecked(_checksum + nativeHit() + governedHit());
            }

            var nativeL1 = new List<LocalRound>();
            var governedL1 = new List<LocalRound>();
            for (var round = 0; round < L1Rounds; round++)
            {
                if (round % 2 == 0)
                {
                    nativeL1.Add(MeasureLocal(nativeHit, deadline.Token));
                    governedL1.Add(MeasureLocal(governedHit, deadline.Token));
                }
                else
                {
                    governedL1.Add(MeasureLocal(governedHit, deadline.Token));
                    nativeL1.Add(MeasureLocal(nativeHit, deadline.Token));
                }
                Console.WriteLine($"Redis benchmark L1 round {round + 1}/{L1Rounds} complete.");
            }

            async Task<double[]> MeasureHybridL2()
            {
                var samples = new double[L2Iterations];
                for (var iteration = 0; iteration < samples.Length; iteration++)
                {
                    var started = Stopwatch.GetTimestamp();
                    var value = await native.GetOrCreateAsync(key.Value, payload, RequireCached, l2Options,
                        cancellationToken: deadline.Token).AsTask().WaitAsync(deadline.Token);
                    samples[iteration] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    if (value != payload) throw new InvalidOperationException("Redis L2 returned a different value.");
                    _checksum = unchecked(_checksum + value.Length);
                }
                return samples;
            }
            async Task<double[]> MeasureDistributed()
            {
                var samples = new double[L2Iterations];
                for (var iteration = 0; iteration < samples.Length; iteration++)
                {
                    var started = Stopwatch.GetTimestamp();
                    var value = await distributed.GetAsync(key.Value, deadline.Token).WaitAsync(deadline.Token);
                    samples[iteration] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    if (value is null || !value.AsSpan().SequenceEqual(raw))
                        throw new InvalidOperationException("Redis raw payload changed during the benchmark.");
                    _checksum = unchecked(_checksum + value.Length);
                }
                return samples;
            }

            // Warm network paths before collecting samples; no source factory is allowed to run.
            _ = await native.GetOrCreateAsync(key.Value, payload, RequireCached, l2Options,
                cancellationToken: deadline.Token).AsTask().WaitAsync(deadline.Token);
            _ = await distributed.GetAsync(key.Value, deadline.Token).WaitAsync(deadline.Token);
            var hybridL2 = new List<double>(L2Rounds * L2Iterations);
            var distributedReads = new List<double>(L2Rounds * L2Iterations);
            for (var round = 0; round < L2Rounds; round++)
            {
                if (round % 2 == 0)
                {
                    hybridL2.AddRange(await MeasureHybridL2());
                    distributedReads.AddRange(await MeasureDistributed());
                }
                else
                {
                    distributedReads.AddRange(await MeasureDistributed());
                    hybridL2.AddRange(await MeasureHybridL2());
                }
                Console.WriteLine($"Redis benchmark L2 round {round + 1}/{L2Rounds} complete.");
            }

            var localResults = new[] { SummarizeLocal("cache.native.l1.hit", nativeL1), SummarizeLocal("cache.governed.l1.hit", governedL1) };
            var sharedResults = new[] { SummarizeShared("cache.native.hybrid.l2.read", hybridL2), SummarizeShared("cache.native.distributed.raw.read", distributedReads) };
            report = new
            {
                MeasuredAtUtc = DateTimeOffset.UtcNow,
                Runtime = RuntimeInformation.FrameworkDescription,
                OS = RuntimeInformation.OSDescription,
                ProcessorCount = Environment.ProcessorCount,
                StopwatchFrequency = Stopwatch.Frequency,
                PayloadUtf8Bytes = payload.Length,
                RawProviderPayloadBytes = raw.Length,
                L1Rounds, L1Iterations, L2Rounds, L2Iterations,
                OperationBudgetSeconds = 55, CleanupBudgetSeconds = 5,
                LocalResults = localResults, SharedResults = sharedResults,
                Notes = "Single-thread sequential warm reads. L1 uses the same prebuilt key, immutable 1 KiB string, policy and HybridCache instance; median batch ns/op and thread allocation B/op. L2 forces local read/write off and rejects source loading. Raw IDistributedCache reads include HybridCache framing but omit its decoding; raw and Hybrid L2 results are not a measurement of governance overhead. L2 samples include client awaiting/deadline guards. No endpoint, connection, prefix or payload is recorded. This run is not a Redis capacity or failover acceptance test."
            };
            foreach (var row in localResults)
                Console.WriteLine($"{row.Scenario,-36} {row.MedianNs,10:F1} ns/op {row.MedianBytes,8:F1} B/op");
            foreach (var row in sharedResults)
                Console.WriteLine($"{row.Scenario,-36} {row.MeanMs,10:F3} ms/op P95={row.P95Ms:F3} ms; reads={row.Samples}");
        }
        catch (Exception error)
        {
            failed = true;
            Console.Error.WriteLine($"Redis benchmark failed ({error.GetType().Name}); connection details are suppressed.");
        }
        finally
        {
            if (distributed is not null && ownedKey is not null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await distributed.RemoveAsync(ownedKey, cleanup.Token).WaitAsync(cleanup.Token);
                    cleanupSucceeded = true;
                }
                catch (Exception error)
                {
                    failed = true;
                    Console.Error.WriteLine($"Redis benchmark exact-key cleanup failed ({error.GetType().Name}); the temporary key retains its bounded TTL.");
                }
            }
            if (provider is not null)
            {
                try { await provider.DisposeAsync(); }
                catch (Exception error)
                {
                    failed = true;
                    Console.Error.WriteLine($"Redis benchmark provider cleanup failed ({error.GetType().Name}).");
                }
            }
        }

        if (failed || report is null || !cleanupSucceeded) return 1;
        try
        {
            output ??= "artifacts/redis-component-performance.json";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Redis benchmark complete; checksum={_checksum}; exact-key cleanup succeeded; report={output}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Redis benchmark report failed ({error.GetType().Name}).");
            return 1;
        }
    }

    private static ValueTask<string> RequireCached(string _, CancellationToken token)
        => throw new InvalidOperationException("Redis benchmark unexpectedly loaded its source.");

    private static LocalRound MeasureLocal(Func<int> read, CancellationToken token)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var iteration = 0; iteration < L1Iterations; iteration++)
        {
            if ((iteration & 4095) == 0) token.ThrowIfCancellationRequested();
            _checksum = unchecked(_checksum + read());
        }
        var elapsed = Stopwatch.GetElapsedTime(started).TotalNanoseconds;
        return new(elapsed / L1Iterations, (double)(GC.GetAllocatedBytesForCurrentThread() - before) / L1Iterations);
    }

    private static LocalResult SummarizeLocal(string scenario, List<LocalRound> rounds)
        => new(scenario, rounds.Select(round => round.Ns).Order().ElementAt(rounds.Count / 2),
            rounds.Select(round => round.Bytes).Order().ElementAt(rounds.Count / 2));

    private static SharedResult SummarizeShared(string scenario, List<double> samples)
    {
        samples.Sort();
        return new(scenario, samples.Average(), samples[(int)Math.Ceiling(samples.Count * .95) - 1], samples.Count);
    }

    private sealed record LocalRound(double Ns, double Bytes);
    private sealed record LocalResult(string Scenario, double MedianNs, double MedianBytes);
    private sealed record SharedResult(string Scenario, double MeanMs, double P95Ms, int Samples);
}
