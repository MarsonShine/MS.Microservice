using System.Diagnostics;
using MS.Microservice.Logging.Core;

const int iterations = 200_000;
const int rounds = 7;

var outer = new RequestLogContext { RequestId = "outer" };
var inner = new RequestLogContext { RequestId = "inner" };

Measure("push/dispose", () =>
{
    using var scope = RequestLogScope.Push(inner);
    return ReferenceEquals(RequestLogScope.Current, inner) ? 1 : 0;
});

using (RequestLogScope.Push(outer))
{
    Measure("nested push/dispose", () =>
    {
        using var scope = RequestLogScope.Push(inner);
        return ReferenceEquals(RequestLogScope.Current, inner) ? 1 : 0;
    });

    Measure("read current", () => ReferenceEquals(RequestLogScope.Current, outer) ? 1 : 0);
}

void Measure(string name, Func<int> operation)
{
    for (var i = 0; i < iterations / 10; i++) _ = operation();

    var times = new double[rounds];
    var allocations = new double[rounds];
    for (var round = 0; round < rounds; round++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        var observed = 0;
        for (var i = 0; i < iterations; i++) observed += operation();
        times[round] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
        allocations[round] = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
        GC.KeepAlive(observed);
    }

    Array.Sort(times);
    Array.Sort(allocations);
    Console.WriteLine($"{name}: {times[rounds / 2]:F1} ns/op, {allocations[rounds / 2]:F1} B/op (median of {rounds} x {iterations:N0})");
}
