using System.Diagnostics;
using MS.Microservice.Infrastructure.Common.NAudio;
using NAudio.Wave;

internal static class InspectionBenchmarks
{
    public static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("audio-inspect-");
        var wav = Path.Combine(directory.FullName, "sample.WAV");
        var unknown = Path.Combine(directory.FullName, "unknown.bin");
        try
        {
            using (var writer = new WaveFileWriter(wav, new WaveFormat(8000, 16, 1)))
                writer.Write(new byte[16_000], 0, 16_000);
            var fileSize = new FileInfo(wav).Length;
            File.WriteAllBytes(unknown, new byte[8192]);
            var processor = new AudioProcessor();
            Measure("DetectActualFormat WAV", () => AudioFileFormatDetector.DetectActualFormat(wav) == AudioFormat.Wav);
            Measure("DetectActualFormat unknown", () => AudioFileFormatDetector.DetectActualFormat(unknown) == AudioFormat.Auto);
            Measure("GetFormatInfo WAV", () =>
            {
                var info = AudioFileFormatDetector.GetFormatInfo(wav);
                return info.DetectedFormat == AudioFormat.Wav && info.ExtensionFormat == AudioFormat.Wav && !info.IsFormatMismatch;
            });
            Measure("GetAudioFileInfo WAV", () =>
            {
                var info = AudioProcessorExtensions.GetAudioFileInfo(wav);
                return info.FileSize == fileSize && info.SampleRate == 8000 && info.Channels == 1
                    && info.BitsPerSample == 16 && info.Duration == TimeSpan.FromSeconds(1) && info.FilePath == wav;
            });
            Measure("ValidateAudioFile WAV", () => processor.ValidateAudioFile(wav));
        }
        finally
        {
            File.Delete(wav);
            File.Delete(unknown);
            directory.Delete();
        }
    }

    private static void Measure(string name, Func<bool> inspect)
    {
        const int iterations = 256;
        const int rounds = 7;
        void RunRound()
        {
            for (var i = 0; i < iterations; i++)
                if (!inspect()) throw new InvalidDataException(name);
        }

        RunRound();
        var times = new double[rounds];
        var allocations = new long[rounds];
        for (var round = 0; round < rounds; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            RunRound();
            times[round] = Stopwatch.GetElapsedTime(start).TotalMicroseconds / iterations;
            allocations[round] = (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
        }
        Array.Sort(times);
        Array.Sort(allocations);
        Console.WriteLine($"{name}: {times[rounds / 2]:F3} us/op, {allocations[rounds / 2]} B/op");
    }
}
