using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;
using MS.Microservice.Infrastructure.Utils;
using MS.Microservice.Infrastructure.Utils.Excel;
using NPOI.HSSF.Util;

CsvCases.Run();
WorkbookCases.Run();
Bench.Measure("Color map construction", 512, () => new ExcelColorMap(),
    map => map.TryGetColor("Black", out var index) && index == HSSFColor.Black.Index);

foreach (bool valid in new[] { true, false })
{
    var input = Encoding.UTF8.GetBytes("Id\n" + string.Concat(Enumerable.Repeat(valid ? "3\n" : "-1\n", 256)));
    var helper = new ExcelHelper();
    Bench.Measure($"CSV annotations {(valid ? "valid" : "invalid")} x256", 12, () =>
    {
        using var stream = new MemoryStream(input, writable: false);
        return helper.ImportCsvWithDataAnnotationsAsync<SampleRow>(stream).GetAwaiter().GetResult();
    }, report => valid
        ? report.Rows.Count == 256 && report.Errors.Count == 0 && report.Rows[^1].RowNumber == 257
        : report.Rows.Count == 0 && report.Errors.Count == 256 && report.Errors[^1].RowNumber == 257);
}

public sealed class SampleRow
{
    [ExcelColumn("Id"), Range(0, 10)] public int Id { get; set; }
}

internal static class Bench
{
    public static void Measure<T>(string name, int iterations, Func<T> run, Func<T, bool> correct)
    {
        T Batch()
        {
            T value = run();
            for (int i = 1; i < iterations; i++) value = run();
            return value;
        }

        if (!correct(Batch())) throw new InvalidDataException(name);
        var times = new double[7];
        var allocations = new double[7];
        for (int round = 0; round < times.Length; round++)
        {
            var before = GC.GetTotalAllocatedBytes(precise: true);
            var started = Stopwatch.GetTimestamp();
            var result = Batch();
            times[round] = Stopwatch.GetElapsedTime(started).TotalMicroseconds / iterations;
            allocations[round] = (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)iterations;
            if (!correct(result)) throw new InvalidDataException(name);
        }
        Array.Sort(times);
        Array.Sort(allocations);
        Console.WriteLine($"{name}: {times[3]:F3} us/op, {allocations[3]:F1} B/op");
    }
}
