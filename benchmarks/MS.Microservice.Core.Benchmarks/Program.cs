using System.Diagnostics;
using MS.Microservice.Core.Text;

if (args is ["--resource-smoke"])
{
    await ResourceCase.RunAsync();
    return;
}

CollectionCases.Run();
QueryCases.Run();
InfrastructureCases.Run();

var common = string.Concat(Enumerable.Range(0, 1024).Select(i => $"shared row {i:D4}\r\n"));
var left = common + "old\n" + common;
var right = common + "new\n" + common;
Bench.Measure("Diff / common edges", 80, () => TextDiff.Compare(left, right),
    lines => lines.Count == 2050 && lines[1024] == new TextDiffLine(TextDiffKind.Removed, "old\n")
        && lines[1025] == new TextDiffLine(TextDiffKind.Added, "new\n"));
var oldLines = string.Concat(Enumerable.Range(0, 96).Select(i => $"old {i}\n"));
var newLines = string.Concat(Enumerable.Range(0, 96).Select(i => $"new {i}\n"));
Bench.Measure("Diff / disjoint", 150, () => TextDiff.Compare(oldLines, newLines),
    lines => lines.Count == 192 && lines.Count(line => line.Kind == TextDiffKind.Removed) == 96);

internal static class Bench
{
    public static void Measure<T>(string name, int iterations, Func<T> operation, Func<T, bool> verify)
    {
        T RunBatch()
        {
            var last = operation();
            for (var i = 1; i < iterations; i++) last = operation();
            return last;
        }

        if (!verify(RunBatch())) throw new InvalidOperationException(name);
        var times = new double[7];
        var allocations = new double[7];
        for (var round = 0; round < times.Length; round++)
        {
            var before = GC.GetTotalAllocatedBytes(precise: true);
            var start = Stopwatch.GetTimestamp();
            var result = RunBatch();
            times[round] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
            allocations[round] = (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)iterations;
            if (!verify(result)) throw new InvalidOperationException(name);
        }
        Array.Sort(times);
        Array.Sort(allocations);
        Console.WriteLine($"{name}: {times[3]:F1} ns/op, {allocations[3]:F1} B/op");
    }
}
