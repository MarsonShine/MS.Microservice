using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using MS.Microservice.Media.Codes;
using MS.Microservice.Media.Inference.BackgroundRemoval;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

if (args.Length == 0)
{
    await MeasureQrAsync(256, 256);
    await MeasureQrAsync(1280, 720);
}
else if (args is ["--birefnet", var modelPath, var portraitPath])
{
    await MeasureBackgroundAsync(modelPath, portraitPath);
}
else
{
    Console.Error.WriteLine("Usage: MS.Microservice.Media.Benchmarks [--birefnet <model.onnx> <portrait>]");
    return 2;
}
return 0;

static async Task MeasureQrAsync(int width, int height)
{
    const string text = "Media benchmark: color QR 42";
    const int rounds = 7;
    const int iterations = 16;
    var processor = new CodeProcessor();
    var options = new CodeReadingOptions
    {
        Formats = [CodeFormat.Qr], AutoRotate = false, TryInverted = false, TryHarder = true,
    };
    using var input = new MemoryStream();
    await processor.GenerateAsync(text, input, new CodeGenerationOptions
    {
        Width = width, Height = height, Foreground = new(15, 60, 99), Background = new(255, 241, 210),
    });

    Task<IReadOnlyList<DecodedCode>> Decode()
    {
        input.Position = 0;
        return processor.DecodeAsync(input, options);
    }

    string Points(IReadOnlyList<DecodedCode> results)
    {
        if (results.Count != 1 || results[0].Format != CodeFormat.Qr || results[0].Text != text ||
            results[0].Points.Count < 3 || results[0].Points.Any(point =>
                !float.IsFinite(point.X) || !float.IsFinite(point.Y) ||
                point.X < 0 || point.X >= width || point.Y < 0 || point.Y >= height))
        {
            throw new InvalidDataException("QR text, format or point bounds changed.");
        }
        return string.Join(" ", results[0].Points.Select(point => $"({point.X:R},{point.Y:R})"));
    }

    var results = await Decode();
    var expected = Points(results);
    var times = new double[rounds];
    var allocations = new double[rounds];
    for (var round = 0; round < rounds; round++)
    {
        var before = GC.GetTotalAllocatedBytes(true);
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            results = await Decode();
        }
        times[round] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / iterations;
        allocations[round] = (GC.GetTotalAllocatedBytes(true) - before) / (double)iterations;
        if (Points(results) != expected)
        {
            throw new InvalidDataException("QR points changed between batches.");
        }
    }

    PrintMedian($"QR {width}x{height}", times, allocations, iterations);
    Console.WriteLine($"text={text}, points={expected}");
}

static async Task MeasureBackgroundAsync(string modelPath, string portraitPath)
{
    const int rounds = 3;
    await using var remover = await BiRefNetBackgroundRemover.CreateAsync(new()
    {
        ModelPath = modelPath,
        CpuThreads = 2,
    });
    using var input = new MemoryStream(await File.ReadAllBytesAsync(portraitPath), writable: false);
    using var foreground = new MemoryStream();
    using var mask = new MemoryStream();

    // The warm-up grows both output buffers and provides the reference PNGs.
    var expected = await remover.RemoveAsync(input, foreground, mask);
    var expectedForeground = PngHash(foreground);
    var expectedMask = PngHash(mask);
    var times = new double[rounds];
    var allocations = new double[rounds];
    for (var round = 0; round < rounds; round++)
    {
        input.Position = 0;
        foreground.SetLength(0);
        mask.SetLength(0);
        var before = GC.GetTotalAllocatedBytes(true);
        var start = Stopwatch.GetTimestamp();
        var result = await remover.RemoveAsync(input, foreground, mask);
        times[round] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        allocations[round] = GC.GetTotalAllocatedBytes(true) - before;
        if (result != expected || PngHash(foreground) != expectedForeground || PngHash(mask) != expectedMask)
        {
            throw new InvalidDataException("BiRefNet output or statistics changed between runs.");
        }
    }

    PrintMedian("BiRefNet, CPU threads=2, soft mask", times, allocations, 1);
    Console.WriteLine($"size={expected.Width}x{expected.Height}, nontransparent={expected.NonTransparentPixels}, meanAlpha={expected.MeanAlpha:R}");
    Console.WriteLine($"foreground SHA256={expectedForeground}");
    Console.WriteLine($"soft mask SHA256={expectedMask}");
}

static string PngHash(MemoryStream output)
{
    var data = output.GetBuffer().AsSpan(0, checked((int)output.Length));
    ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
    if (!data.StartsWith(signature))
    {
        throw new InvalidDataException("Expected PNG output.");
    }
    return Convert.ToHexString(SHA256.HashData(data));
}

static void PrintMedian(string name, double[] times, double[] allocations, int iterations)
{
    Array.Sort(times);
    Array.Sort(allocations);
    var mid = times.Length / 2;
    Console.WriteLine($"{name}: {times[mid]:G3} ms/op, {allocations[mid]:F0} managed B/op (median of {times.Length} x {iterations})");
}
