using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Caching;
using MS.Microservice.Infrastructure.Telemetry.Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class CacheMetricsIntegrationTests
{
    [Fact]
    public async Task SharedTelemetryRegistrationExportsGovernedCacheInstruments()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenTelemetry:ServiceName"] = "cache-test", ["OpenTelemetry:ServiceVersion"] = "1",
            ["OpenTelemetry:EnvironmentName"] = "Test", ["OpenTelemetry:ActivitySourceName"] = "cache-test"
        }).Build();
        var services = new ServiceCollection(); services.AddLogging(); services.AddGovernedCaching();
        var exporter = new CaptureExporter();
        services.AddMsOpenTelemetry(configuration);
        services.AddOpenTelemetry().WithMetrics(builder => builder.AddReader(new PeriodicExportingMetricReader(exporter, 60000)));
        using var provider = services.BuildServiceProvider();
        var metrics = provider.GetRequiredService<MeterProvider>();
        var cache = provider.GetRequiredService<IGovernedCache>();
        var key = cache.CreateKey<string>("metrics-test.v1", "owner", "sample");
        await cache.GetOrCreateAsync(key, _ => ValueTask.FromResult("value"));
        await cache.GetOrCreateAsync(key, _ => ValueTask.FromResult("unexpected"));
        Assert.True(metrics.ForceFlush());
        Assert.Contains("cache.requests", exporter.Names); Assert.Contains("cache.source.loads", exporter.Names);
        Assert.Contains("cache.source.duration", exporter.Names);
    }
    private sealed class CaptureExporter : BaseExporter<Metric>
    {
        internal ConcurrentBag<string> Names { get; } = [];
        public override ExportResult Export(in Batch<Metric> batch)
        { foreach (var metric in batch) Names.Add(metric.Name); return ExportResult.Success; }
    }
}
