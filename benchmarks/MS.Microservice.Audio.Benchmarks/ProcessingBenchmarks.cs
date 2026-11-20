using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using MS.Microservice.Infrastructure.Common.NAudio;
using NAudio.Wave;

internal static class ProcessingBenchmarks
{
    private const int SampleRate = 44100;

    public static async Task RunAsync()
    {
        var directory = Directory.CreateTempSubdirectory("audio-bench-");
        try
        {
            var mono = Path.Combine(directory.FullName, "mono.wav");
            var stereo = Path.Combine(directory.FullName, "stereo.wav");
            var output = Path.Combine(directory.FullName, "output.wav");
            const int frames = SampleRate * 8 + 17;
            File.WriteAllBytes(mono, CreatePcm(frames, 1, 1));
            File.WriteAllBytes(stereo, CreatePcm(frames, 2, 2));

            var processor = new AudioProcessor();
            var editor = new AudioEditor();
            string[] mixInputs = [mono, stereo];
            var gain = new AudioEditOptions { Gain = 1.375f };
            var fades = gain with { FadeIn = TimeSpan.FromSeconds(1.5), FadeOut = TimeSpan.FromSeconds(2.25) };
            var segments = Enumerable.Range(0, 128).Select(i => CreatePcm(127 + i, 1, i)).ToArray();
            var combine = new AudioCombineOptions { TargetFormat = new WaveFormat(SampleRate, 16, 1), SilenceDuration = 0 };

            await MeasureAsync("Mix two WAVs", output, () => processor.MixAudioFilesAsync(mixInputs, output));
            await MeasureAsync("Mono to stereo WAV", output, () => processor.ConvertMonoToStereoAsync(mono, output));
            await MeasureAsync("Edit WAV gain", output, () => new ValueTask(editor.EditAsync(stereo, output, gain)));
            await MeasureAsync("Edit WAV gain and fades", output, () => new ValueTask(editor.EditAsync(stereo, output, fades)));
            await MeasureAsync("Combine 128 PCM segments", output, () => processor.CombineAudioDataAsync(segments, output, combine));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static byte[] CreatePcm(int frames, int channels, int seed)
    {
        var samples = new byte[frames * channels * sizeof(short)];
        for (var i = 0; i < frames * channels; i++)
        {
            var value = unchecked((short)(i * 173 + seed * 911));
            BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(i * sizeof(short)), value);
        }

        using var stream = new MemoryStream();
        using (var writer = new WaveFileWriter(stream, new WaveFormat(SampleRate, 16, channels)))
            writer.Write(samples, 0, samples.Length);
        return stream.ToArray();
    }

    private static async Task MeasureAsync(string name, string output, Func<ValueTask> operation)
    {
        const int rounds = 7;
        const int operationsPerRound = 4;
        for (var i = 0; i < operationsPerRound; i++)
        {
            File.Delete(output);
            await operation();
        }

        var milliseconds = new double[rounds];
        var allocations = new long[rounds];
        for (var round = 0; round < rounds; round++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            for (var i = 0; i < operationsPerRound; i++)
            {
                File.Delete(output);
                // The public methods dispatch work to the thread pool.
                var allocated = GC.GetTotalAllocatedBytes(precise: true);
                var start = Stopwatch.GetTimestamp();
                await operation();
                milliseconds[round] += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocations[round] += GC.GetTotalAllocatedBytes(precise: true) - allocated;
            }
        }

        Array.Sort(milliseconds);
        Array.Sort(allocations);
        using var result = File.OpenRead(output);
        var hash = Convert.ToHexString(SHA256.HashData(result));
        Console.WriteLine($"{name}: {milliseconds[rounds / 2] / operationsPerRound:F3} ms/op, " +
                          $"{(double)allocations[rounds / 2] / operationsPerRound:F0} B/op, SHA256 {hash}");
    }
}
