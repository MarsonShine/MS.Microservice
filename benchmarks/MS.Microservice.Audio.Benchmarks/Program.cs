using System.Diagnostics;
using MS.Microservice.Infrastructure.Common.NAudio;

const int iterations = 100_000;
const int rounds = 5;

using var wav = new MemoryStream([0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45]);
using var id3 = new MemoryStream([0x49, 0x44, 0x33, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
using var unknown = new MemoryStream(new byte[1024]);

Measure("WAV header", wav);
Measure("MP3 ID3 header", id3);
Measure("unknown 1024-byte header", unknown);

void Measure(string name, Stream stream)
{
    for (var i = 0; i < iterations / 10; i++) _ = AudioFileFormatDetector.DetectFormatFromStream(stream);

    var times = new double[rounds];
    var allocations = new double[rounds];
    for (var round = 0; round < rounds; round++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        var result = AudioFormat.Auto;
        for (var i = 0; i < iterations; i++)
            result = AudioFileFormatDetector.DetectFormatFromStream(stream);
        times[round] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
        allocations[round] = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
        GC.KeepAlive(result);
    }

    Array.Sort(times);
    Array.Sort(allocations);
    Console.WriteLine($"{name}: {times[rounds / 2]:F1} ns/op, {allocations[rounds / 2]:F1} B/op");
}
