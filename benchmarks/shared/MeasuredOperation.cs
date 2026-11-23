using System.Diagnostics;
using System.Globalization;

internal static class MeasuredOperation
{
    public static void Run<T>(string name, int iterations, Func<T> execute, Func<T, bool> verify)
    {
        T Batch()
        {
            var result = execute();
            for (int i = 1; i < iterations; i++)
            {
                result = execute();
            }
            return result;
        }

        if (!verify(Batch()))
        {
            throw new InvalidDataException(name);
        }
        var times = new double[7];
        var allocations = new double[times.Length];
        for (int round = 0; round < times.Length; round++)
        {
            long before = GC.GetTotalAllocatedBytes(precise: true);
            long started = Stopwatch.GetTimestamp();
            var result = Batch();
            times[round] = Stopwatch.GetElapsedTime(started).TotalMicroseconds / iterations;
            allocations[round] = (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)iterations;
            if (!verify(result))
            {
                throw new InvalidDataException(name);
            }
        }
        Array.Sort(times);
        Array.Sort(allocations);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{name}: {times[3]:F3} us/op, {allocations[3]:F1} B/op"));
    }
}
