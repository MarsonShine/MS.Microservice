using System.Diagnostics;
using System.Diagnostics.Metrics;
using MS.Microservice.Infrastructure.Telemetry;

const int iterations = 200_000;
const int rounds = 5;

using var metrics = new PlatformMetrics();
Measure("no listener, inbox processed", () => metrics.RecordInboxProcessed(1.5));
Measure("no listener, outbox dead letter", () => metrics.RecordOutboxFailed(1.5, true));

using var listener = new MeterListener
{
    InstrumentPublished = (instrument, current) =>
    {
        if (instrument.Meter.Name == PlatformMetrics.MeterName)
            current.EnableMeasurementEvents(instrument);
    }
};
listener.SetMeasurementEventCallback<double>(static (_, _, _, _) => { });
listener.Start();
Measure("listener, inbox processed", () => metrics.RecordInboxProcessed(1.5));
Measure("listener, outbox dead letter", () => metrics.RecordOutboxFailed(1.5, true));

void Measure(string name, Action record)
{
    for (var i = 0; i < iterations / 10; i++) record();

    var times = new double[rounds];
    var allocations = new double[rounds];
    for (var round = 0; round < rounds; round++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++) record();
        times[round] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
        allocations[round] = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
    }

    Array.Sort(times);
    Array.Sort(allocations);
    Console.WriteLine($"{name}: {times[rounds / 2]:F1} ns/op, {allocations[rounds / 2]:F1} B/op");
}
