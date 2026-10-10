using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MS.Microservice.Media.Images;

namespace MS.Microservice.Media.Inference.BackgroundRemoval;

/// <summary>Verified local BiRefNet-lite CPU inference; reuse and asynchronously dispose one serialized session.</summary>
public sealed class BiRefNetBackgroundRemover : IAsyncDisposable
{
    public const string ModelId = "onnx-community/BiRefNet_lite-ONNX@de15b22ba131738a16dff04aab8bdf8dc32e3ac1";
    public const string ModelSha256 = "5600024376F572A557870A5EB0AFB1E5961636BEF4E1E22132025467D0F03333";
    internal const int ModelBytes = 224_005_088;
    internal const int ModelSize = 1024;
    private readonly InferenceSession _session;
    private readonly ImageLimits _limits;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposeRequested;
    private bool _disposed;

    private BiRefNetBackgroundRemover(InferenceSession session, ImageLimits limits) { _session = session; _limits = limits; }

    public static async Task<BiRefNetBackgroundRemover> CreateAsync(BiRefNetBackgroundRemovalOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await VerifiedOnnxModel.ReadAsync(options.ModelPath, ModelBytes, ModelSha256, cancellationToken).ConfigureAwait(false);
        using var settings = new SessionOptions
        {
            IntraOpNumThreads = options.CpuThreads, InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            EnableCpuMemArena = false, EnableMemoryPattern = false
        };
        var session = await Task.Run(() => new InferenceSession(bytes, settings), cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (session.InputMetadata.Count != 1 || session.OutputMetadata.Count != 1
                || !session.InputMetadata.TryGetValue("input_image", out var input) || !FloatShape(input, 3)
                || !session.OutputMetadata.TryGetValue("output_image", out var output) || !FloatShape(output, 1))
                throw new InvalidDataException("The supported model requires fixed float NCHW 1024 RGB input and single-channel mask logits.");
            return new(session, options.Limits with { });
        }
        catch { session.Dispose(); throw; }
    }

    /// <summary>Writes transparent PNG and optional opaque grayscale PNG containing its final effective alpha. Streams stay open; failures may leave partial output.</summary>
    public async Task<BackgroundRemovalResult> RemoveAsync(Stream inputImage, Stream transparentPng, Stream? softMaskPng = null, CancellationToken cancellationToken = default)
    {
        ValidateStreams(inputImage, transparentPng, softMaskPng);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
            using var source = await ImageCodec.DecodeAsync(inputImage, _limits, cancellationToken).ConfigureAwait(false);
            var tensor = BackgroundPixels.CreateTensor(source, ModelSize, cancellationToken);
            using var input = OrtValue.CreateTensorValueFromMemory(tensor, [1, 3, ModelSize, ModelSize]);
            using var run = new RunOptions();
            using var cancellation = cancellationToken.Register(static state => ((RunOptions)state!).Terminate = true, run);
            IDisposableReadOnlyCollection<OrtValue> values;
            try { values = await Task.Run(() => _session.Run(run, ["input_image"], [input], ["output_image"])).ConfigureAwait(false); }
            catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
            using (values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = values.Single();
                var shape = value.GetTensorTypeAndShape();
                if (shape.ElementDataType != TensorElementType.Float || !shape.Shape.SequenceEqual(new long[] { 1, 1, ModelSize, ModelSize }))
                    throw new InvalidDataException("The model returned an unexpected mask tensor.");
                var probabilities = BackgroundPixels.Probabilities(value.GetTensorDataAsSpan<float>(), ModelSize, cancellationToken);
                using var output = BackgroundPixels.CreateStraightBitmap(source.Width, source.Height);
                using var mask = softMaskPng is null ? null : ImageCodec.CreateBitmap(source.Width, source.Height, _limits);
                var statistics = BackgroundPixels.Compose(source, output, mask, probabilities, ModelSize, cancellationToken);
                await ImageCodec.EncodeAsync(output, transparentPng, ImageOutputFormat.Png, 100, new(255, 255, 255), cancellationToken).ConfigureAwait(false);
                if (mask is not null)
                    await ImageCodec.EncodeAsync(mask, softMaskPng!, ImageOutputFormat.Png, 100, new(255, 255, 255), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return new(source.Width, source.Height, statistics.NonTransparent, statistics.MeanAlpha, ModelId);
            }
        }
        finally { _gate.Release(); }
    }

    internal static void ValidateStreams(Stream input, Stream output, Stream? mask)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (ReferenceEquals(input, output) || ReferenceEquals(input, mask) || ReferenceEquals(output, mask)
            || !input.CanRead || !output.CanWrite || mask is { CanWrite: false })
            throw new ArgumentException("Use distinct readable input and writable output streams.");
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposeRequested, 1);
        await _gate.WaitAsync().ConfigureAwait(false);
        try { if (!_disposed) { _disposed = true; _session.Dispose(); } }
        finally { _gate.Release(); }
    }

    private static bool FloatShape(NodeMetadata metadata, int channels) => metadata.IsTensor && metadata.ElementType == typeof(float)
        && metadata.Dimensions.SequenceEqual(new[] { 1, channels, ModelSize, ModelSize });
}
