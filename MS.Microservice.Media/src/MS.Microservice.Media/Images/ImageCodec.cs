using SkiaSharp;

namespace MS.Microservice.Media.Images;

internal static class ImageCodec
{
    internal static async Task<SKBitmap> DecodeAsync(Stream input, ImageLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateLimits(limits);
        if (!input.CanRead) throw new ArgumentException("Input must be readable.", nameof(input));
        cancellationToken.ThrowIfCancellationRequested();
        using var encoded = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0,
            (int)Math.Min(buffer.Length, (long)limits.MaximumEncodedBytes - encoded.Length + 1)), cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (encoded.Length + read > limits.MaximumEncodedBytes)
                throw new InvalidDataException("Encoded image exceeds the input byte limit.");
            encoded.Write(buffer, 0, read);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var data = SKData.CreateCopy(encoded.GetBuffer().AsSpan(0, (int)encoded.Length));
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Input is not a supported image.");
        CheckDimensions(codec.Info.Width, codec.Info.Height, limits);
        if (codec.FrameCount > 1) throw new NotSupportedException("Animated images require an animation processor.");
        var decoded = CreateBitmap(codec.Info.Width, codec.Info.Height, limits);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success)
                throw new InvalidDataException("Image pixels are incomplete or invalid.");
            cancellationToken.ThrowIfCancellationRequested();
            var (rotation, flipX, flipY) = codec.EncodedOrigin switch
            {
                SKEncodedOrigin.TopRight => (0, true, false),
                SKEncodedOrigin.BottomRight => (180, false, false),
                SKEncodedOrigin.BottomLeft => (0, false, true),
                SKEncodedOrigin.LeftTop => (90, true, false),
                SKEncodedOrigin.RightTop => (90, false, false),
                SKEncodedOrigin.RightBottom => (90, false, true),
                SKEncodedOrigin.LeftBottom => (270, false, false),
                _ => (0, false, false)
            };
            if (rotation == 0 && !flipX && !flipY) return decoded;
            var oriented = Transform(decoded, rotation, flipX, flipY, limits, cancellationToken);
            decoded.Dispose();
            return oriented;
        }
        catch { decoded.Dispose(); throw; }
    }

    internal static async Task EncodeAsync(
        SKBitmap bitmap, Stream output, ImageOutputFormat format, int quality,
        ImageColor jpegBackground, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(jpegBackground);
        if (!output.CanWrite) throw new ArgumentException("Output must be writable.", nameof(output));
        if (!Enum.IsDefined(format) || quality is < 0 or > 100 || jpegBackground.Alpha != 255)
            throw new ArgumentException("Choose a supported format, quality from 0 to 100 and an opaque JPEG background.");
        cancellationToken.ThrowIfCancellationRequested();
        SKBitmap? flattened = null;
        try
        {
            if (format == ImageOutputFormat.Jpeg)
            {
                flattened = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
                using var canvas = new SKCanvas(flattened);
                canvas.Clear(new SKColor(jpegBackground.Red, jpegBackground.Green, jpegBackground.Blue));
                using var foreground = SKImage.FromBitmap(bitmap);
                canvas.DrawImage(foreground, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
            }

            using var image = SKImage.FromBitmap(flattened ?? bitmap);
            using var encoded = image.Encode(format switch
            {
                ImageOutputFormat.Png => SKEncodedImageFormat.Png,
                ImageOutputFormat.Jpeg => SKEncodedImageFormat.Jpeg,
                _ => SKEncodedImageFormat.Webp
            }, quality) ?? throw new InvalidOperationException("Image encoding failed.");
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = encoded.AsStream();
            await stream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
        finally { flattened?.Dispose(); }
    }

    internal static SKBitmap Transform(
        SKBitmap source, int rotation, bool flipX, bool flipY, ImageLimits limits, CancellationToken cancellationToken)
    {
        var swap = rotation is 90 or 270;
        var result = CreateBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height, limits);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(result.Width / 2f, result.Height / 2f);
            canvas.Scale(flipX ? -1 : 1, flipY ? -1 : 1);
            canvas.RotateDegrees(rotation);
            canvas.Translate(-source.Width / 2f, -source.Height / 2f);
            using var image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    internal static void ValidateLimits(ImageLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.MaximumDimension < 1 || limits.MaximumPixels < 1 || limits.MaximumEncodedBytes < 1)
            throw new ArgumentException("Image limits must be positive.", nameof(limits));
    }

    internal static void CheckDimensions(int width, int height, ImageLimits limits)
    {
        if (width < 1 || height < 1 || width > limits.MaximumDimension || height > limits.MaximumDimension
            || (long)width * height > limits.MaximumPixels || (long)width * height > int.MaxValue / 4)
            throw new InvalidDataException("Image dimensions exceed the configured raster limits.");
    }

    internal static SKBitmap CreateBitmap(int width, int height, ImageLimits limits)
    {
        CheckDimensions(width, height, limits);
        var bitmap = new SKBitmap();
        if (bitmap.TryAllocPixels(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul)))
            return bitmap;
        bitmap.Dispose();
        throw new OutOfMemoryException("Cannot allocate image pixels.");
    }
}
