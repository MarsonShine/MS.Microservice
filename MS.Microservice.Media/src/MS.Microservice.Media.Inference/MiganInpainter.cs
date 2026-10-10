using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MS.Microservice.Media.Images;

namespace MS.Microservice.Media.Inference;

/// <summary>One locally provisioned, verified MI-GAN CPU session; requests are serialized per instance.</summary>
public sealed class MiganInpainter : IAsyncDisposable
{
    public const string ModelId = "MI-GAN-512-Places2/pipeline-v2@406830d0fa60666da0071c342ad2fbc8f30c5c64";
    public const string ModelSha256 = "6F1F3530A1A2324B19752018CE756088B07973CDA8D7D890034ACE5C8A48C40B";
    internal const int ModelBytes = 28_079_181;
    private readonly InferenceSession _session;
    private readonly ImageLimits _limits;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;
    private int _disposeRequested;

    private MiganInpainter(InferenceSession session, ImageLimits limits) { _session = session; _limits = limits; }

    /// <summary>Loads a verified model snapshot. Performs no network requests or automatic downloads.</summary>
    public static async Task<MiganInpainter> CreateAsync(MiganInpaintingOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var model = await ReadVerifiedModelAsync(options.ModelPath, cancellationToken).ConfigureAwait(false);
        using var sessionOptions = new SessionOptions
        {
            IntraOpNumThreads = options.CpuThreads, InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };
        var session = await Task.Run(() => new InferenceSession(model, sessionOptions), cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateMetadata(session);
            return new MiganInpainter(session, options.Limits);
        }
        catch { session.Dispose(); throw; }
    }

    /// <summary>
    /// Repairs white regions of a matching opaque binary mask, writing PNG with alpha and unselected normalized pixels preserved.
    /// Caller streams must use distinct resources and remain open; failure or cancellation may leave partial output.
    /// </summary>
    public async Task<InpaintingResult> InpaintAsync(
        Stream inputImage, Stream grayscaleMask, Stream outputPng, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputImage);
        ArgumentNullException.ThrowIfNull(grayscaleMask);
        ArgumentNullException.ThrowIfNull(outputPng);
        if (ReferenceEquals(inputImage, grayscaleMask) || ReferenceEquals(inputImage, outputPng) || ReferenceEquals(grayscaleMask, outputPng))
            throw new ArgumentException("Input image, mask and output must be distinct streams.");
        if (!inputImage.CanRead || !grayscaleMask.CanRead || !outputPng.CanWrite)
            throw new ArgumentException("Image and mask must be readable and output writable.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
            using var image = await ImageCodec.DecodeAsync(inputImage, _limits, cancellationToken).ConfigureAwait(false);
            byte[] edits;
            int selected;
            using (var mask = await ImageCodec.DecodeAsync(grayscaleMask, _limits, cancellationToken).ConfigureAwait(false))
                (edits, selected) = InpaintingPixels.ReadMask(mask, image.Width, image.Height, cancellationToken);
            var edited = 0;
            if (selected > 0)
            {
                if (image.Width < 3 || image.Height < 3)
                    throw new ArgumentException("Nonempty MI-GAN repair requires width and height of at least 3 for reflection padding.");
                var rgb = InpaintingPixels.CreateRgb(image, cancellationToken);
                var known = InpaintingPixels.CreateKnownMask(edits, image.Width, image.Height, cancellationToken);
                using var imageValue = OrtValue.CreateTensorValueFromMemory(rgb, [1, 3, image.Height, image.Width]);
                using var maskValue = OrtValue.CreateTensorValueFromMemory(known, [1, 1, image.Height, image.Width]);
                using var runOptions = new RunOptions();
                cancellationToken.ThrowIfCancellationRequested();
                using var results = await Task.Run(() => _session.Run(runOptions,
                    ["image", "mask"], [imageValue, maskValue], ["result"]), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var result = results.Single();
                var shape = result.GetTensorTypeAndShape();
                if (shape.ElementDataType != TensorElementType.UInt8 || !shape.Shape.SequenceEqual(new long[] { 1, 3, image.Height, image.Width }))
                    throw new InvalidDataException("The model returned an unexpected NCHW shape.");
                edited = InpaintingPixels.Compose(image, edits, result.GetTensorDataAsSpan<byte>(), cancellationToken);
            }
            await ImageCodec.EncodeAsync(image, outputPng, ImageOutputFormat.Png, 100, new ImageColor(255, 255, 255), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new InpaintingResult(image.Width, image.Height, selected, edited, ModelId);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposeRequested, 1);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
        }
        finally { _gate.Release(); }
        // Keep the managed gate usable for already queued callers to observe disposal; no WaitHandle is allocated.
    }

    internal static async Task<byte[]> ReadVerifiedModelAsync(string path, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length != ModelBytes) throw new InvalidDataException("The model size does not match the supported MI-GAN pipeline.");
        var bytes = new byte[ModelBytes];
        await file.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (file.Length != ModelBytes || Convert.ToHexString(SHA256.HashData(bytes)) != ModelSha256)
            throw new InvalidDataException("The model SHA-256 does not match the supported MI-GAN pipeline.");
        return bytes;
    }

    private static void ValidateMetadata(InferenceSession session)
    {
        if (session.InputMetadata.Count != 2 || session.OutputMetadata.Count != 1
            || !session.InputMetadata.TryGetValue("image", out var image) || !session.InputMetadata.TryGetValue("mask", out var mask)
            || !session.OutputMetadata.TryGetValue("result", out var result) || !IsTensor(image, 3) || !IsTensor(mask, 1) || !IsTensor(result, 3))
            throw new InvalidDataException("Expected uint8 NCHW image/mask inputs and one result output with dynamic spatial dimensions.");

        static bool IsTensor(NodeMetadata value, int channels) => value.IsTensor && value.ElementType == typeof(byte)
            && value.Dimensions.SequenceEqual(new[] { -1, channels, -1, -1 });
    }
}
