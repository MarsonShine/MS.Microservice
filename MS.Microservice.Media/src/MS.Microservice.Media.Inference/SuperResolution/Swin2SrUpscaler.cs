using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MS.Microservice.Media.Images;
using SkiaSharp;

namespace MS.Microservice.Media.Inference.SuperResolution;

/// <summary>A verified local Swin2SR CPU session for tiled twofold super-resolution. Reuse and asynchronously dispose the instance.</summary>
public sealed class Swin2SrUpscaler : IAsyncDisposable
{
    public const string ModelId = "Xenova/swin2SR-lightweight-x2-64@92a21aca5713f20faf9a87590cdfbdce2e34112c";
    public const string ModelSha256 = "C2ABBFE0CC8E685B5E11964970F8EBE3D24072E904FD5545A30AC31EC1E110DB";
    internal const int ModelBytes = 8_078_888;
    private readonly InferenceSession _session;
    private readonly Swin2SrOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposeRequested;
    private bool _disposed;

    private Swin2SrUpscaler(InferenceSession session, Swin2SrOptions options) { _session = session; _options = options; }

    public static async Task<Swin2SrUpscaler> CreateAsync(Swin2SrOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ModelPath);
        ImageCodec.ValidateLimits(options.Limits);
        if (options.CpuThreads is < 1 or > 64 || options.MaximumOutputDimension < 1 || options.MaximumOutputPixels < 1
            || options.TileSize is < 32 or > 256 || options.TileSize % 8 != 0
            || options.ContextPixels is < 0 or > 64 || options.ContextPixels % 8 != 0 || 2 * options.ContextPixels > options.TileSize)
            throw new ArgumentException("Invalid CPU, raster or tile budgets.", nameof(options));
        var bytes = await VerifiedOnnxModel.ReadAsync(options.ModelPath, ModelBytes, ModelSha256, cancellationToken).ConfigureAwait(false);
        using var settings = new SessionOptions
        { IntraOpNumThreads = options.CpuThreads, InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
        var session = await Task.Run(() => new InferenceSession(bytes, settings), cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (session.InputMetadata.Count != 1 || session.OutputMetadata.Count != 1
                || !session.InputMetadata.TryGetValue("pixel_values", out var input)
                || !session.OutputMetadata.TryGetValue("reconstruction", out var output)
                || !FloatNchw(input) || !FloatNchw(output))
                throw new InvalidDataException("The supported model requires float NCHW input and reconstruction.");
            return new(session, options with { Limits = options.Limits with { } });
        }
        catch { session.Dispose(); throw; }
    }

    /// <summary>Writes a twofold PNG with bilinearly scaled source alpha; streams remain open. Failure or cancellation may leave partial output.</summary>
    public async Task<SuperResolutionResult> UpscaleAsync(Stream inputImage, Stream outputPng, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputImage);
        ArgumentNullException.ThrowIfNull(outputPng);
        if (ReferenceEquals(inputImage, outputPng) || !inputImage.CanRead || !outputPng.CanWrite)
            throw new ArgumentException("Use distinct readable input and writable output streams.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
            using var input = await ImageCodec.DecodeAsync(inputImage, _options.Limits, cancellationToken).ConfigureAwait(false);
            using var output = ImageCodec.CreateBitmap(checked(input.Width * 2), checked(input.Height * 2),
                new ImageLimits { MaximumDimension = _options.MaximumOutputDimension, MaximumPixels = _options.MaximumOutputPixels });
            var tiles = 0;
            for (var y = 0; y < input.Height; y += _options.TileSize)
                for (var x = 0; x < input.Width; x += _options.TileSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var width = Math.Min(_options.TileSize, input.Width - x);
                    var height = Math.Min(_options.TileSize, input.Height - y);
                    var left = Math.Max(0, x - _options.ContextPixels);
                    var top = Math.Max(0, y - _options.ContextPixels);
                    var regionWidth = Math.Min(input.Width, x + width + _options.ContextPixels) - left;
                    var regionHeight = Math.Min(input.Height, y + height + _options.ContextPixels) - top;
                    var paddedWidth = Math.Max(8, (regionWidth + 7) / 8 * 8);
                    var paddedHeight = Math.Max(8, (regionHeight + 7) / 8 * 8);
                    var tensor = CreateTensor(input, left, top, regionWidth, regionHeight, paddedWidth, paddedHeight, cancellationToken);
                    using var inputValue = OrtValue.CreateTensorValueFromMemory(tensor, [1, 3, paddedHeight, paddedWidth]);
                    using var runOptions = new RunOptions();
                    using var cancel = cancellationToken.Register(static state => ((RunOptions)state!).Terminate = true, runOptions);
                    IDisposableReadOnlyCollection<OrtValue> results;
                    try
                    {
                        results = await Task.Run(() => _session.Run(runOptions, ["pixel_values"], [inputValue], ["reconstruction"])).ConfigureAwait(false);
                    }
                    catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested)
                    { throw new OperationCanceledException(cancellationToken); }
                    using (results)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var value = results.Single();
                        var shape = value.GetTensorTypeAndShape();
                        if (shape.ElementDataType != TensorElementType.Float
                            || !shape.Shape.SequenceEqual(new long[] { 1, 3, paddedHeight * 2, paddedWidth * 2 }))
                            throw new InvalidDataException("The model returned an unexpected twofold raster.");
                        Compose(input, output, value.GetTensorDataAsSpan<float>(), x, y, width, height,
                            (x - left) * 2, (y - top) * 2, paddedWidth * 2, paddedHeight * 2, cancellationToken);
                    }
                    tiles++;
                }
            await ImageCodec.EncodeAsync(output, outputPng, ImageOutputFormat.Png, 100, new(255, 255, 255), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new(input.Width, input.Height, output.Width, output.Height, tiles, ModelId);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposeRequested, 1);
        await _gate.WaitAsync().ConfigureAwait(false);
        try { if (!_disposed) { _disposed = true; _session.Dispose(); } }
        finally { _gate.Release(); }
    }

    private static bool FloatNchw(NodeMetadata value) => value.IsTensor && value.ElementType == typeof(float) && value.Dimensions.Length == 4;

    internal static int Reflect(int index, int length)
    {
        if (length == 1) return 0;
        var period = 2 * (length - 1);
        var remainder = index % period;
        return remainder < length ? remainder : period - remainder;
    }

    private static float[] CreateTensor(SKBitmap source, int left, int top, int width, int height, int paddedWidth, int paddedHeight, CancellationToken token)
    {
        var plane = paddedWidth * paddedHeight;
        var tensor = new float[plane * 3];
        for (var y = 0; y < paddedHeight; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < paddedWidth; x++)
            {
                var pixel = source.GetPixel(left + Reflect(x, width), top + Reflect(y, height));
                var alpha = pixel.Alpha / 255f;
                var index = y * paddedWidth + x;
                tensor[index] = pixel.Red / 255f * alpha + 1 - alpha;
                tensor[plane + index] = pixel.Green / 255f * alpha + 1 - alpha;
                tensor[2 * plane + index] = pixel.Blue / 255f * alpha + 1 - alpha;
            }
        }
        return tensor;
    }

    private static void Compose(SKBitmap source, SKBitmap output, ReadOnlySpan<float> rgb, int x, int y, int width, int height,
        int cropX, int cropY, int rasterWidth, int rasterHeight, CancellationToken token)
    {
        foreach (var value in rgb)
            if (!float.IsFinite(value)) throw new InvalidDataException("The model returned a non-finite pixel.");
        var plane = rasterWidth * rasterHeight;
        for (var row = 0; row < height * 2; row++)
        {
            token.ThrowIfCancellationRequested();
            for (var column = 0; column < width * 2; column++)
            {
                var index = (cropY + row) * rasterWidth + cropX + column;
                var outputX = x * 2 + column;
                var outputY = y * 2 + row;
                output.SetPixel(outputX, outputY, new(ToByte(rgb[index]), ToByte(rgb[plane + index]), ToByte(rgb[2 * plane + index]),
                    ScaleAlpha(source, outputX, outputY)));
            }
        }
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(Math.Clamp(value, 0, 1) * 255), 0, 255);

    internal static byte ScaleAlpha(SKBitmap source, int x, int y)
    {
        var sourceX = Math.Clamp((x + 0.5) / 2 - 0.5, 0, source.Width - 1);
        var sourceY = Math.Clamp((y + 0.5) / 2 - 0.5, 0, source.Height - 1);
        var left = (int)sourceX;
        var top = (int)sourceY;
        var right = Math.Min(source.Width - 1, left + 1);
        var bottom = Math.Min(source.Height - 1, top + 1);
        var dx = sourceX - left;
        var dy = sourceY - top;
        var upper = source.GetPixel(left, top).Alpha * (1 - dx) + source.GetPixel(right, top).Alpha * dx;
        var lower = source.GetPixel(left, bottom).Alpha * (1 - dx) + source.GetPixel(right, bottom).Alpha * dx;
        return (byte)Math.Clamp((int)Math.Round(upper * (1 - dy) + lower * dy), 0, 255);
    }
}
