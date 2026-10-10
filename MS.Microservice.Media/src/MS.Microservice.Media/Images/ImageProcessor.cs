using SkiaSharp;

namespace MS.Microservice.Media.Images;

public sealed class ImageProcessor
{
    public async Task<ImageProcessingResult> ProcessAsync(
        Stream input, Stream output, ImageProcessingOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (ReferenceEquals(input, output)) throw new ArgumentException("Input and output must be different streams.", nameof(output));
        if (!output.CanWrite) throw new ArgumentException("Output must be writable.", nameof(output));
        options ??= new ImageProcessingOptions();
        ImageCodec.ValidateLimits(options.Limits);
        ArgumentNullException.ThrowIfNull(options.JpegBackground);
        if (!Enum.IsDefined(options.Format) || !Enum.IsDefined(options.ResizeMode)
            || options.Quality is < 0 or > 100 || options.RotationDegrees is not (0 or 90 or 180 or 270)
            || options.JpegBackground.Alpha != 255)
            throw new ArgumentException("Invalid image format, resize mode, quality, quarter-turn or JPEG background.", nameof(options));
        if (options.Resize is { } target)
            ImageCodec.CheckDimensions(target.Width, target.Height, options.Limits);
        var current = await ImageCodec.DecodeAsync(input, options.Limits, cancellationToken).ConfigureAwait(false);
        try
        {
            if (options.Crop is { } crop)
            {
                if (crop.X < 0 || crop.Y < 0 || crop.Width < 1 || crop.Height < 1
                    || (long)crop.X + crop.Width > current.Width || (long)crop.Y + crop.Height > current.Height)
                    throw new ArgumentException("Crop must be contained in the oriented image.", nameof(options));
                var next = Draw(current, new SKRect(crop.X, crop.Y, crop.X + crop.Width, crop.Y + crop.Height),
                    crop.Width, crop.Height, options.Limits, SKFilterMode.Nearest, cancellationToken);
                current.Dispose();
                current = next;
            }

            if (options.RotationDegrees != 0 || options.FlipHorizontal || options.FlipVertical)
            {
                var next = ImageCodec.Transform(current, options.RotationDegrees, options.FlipHorizontal,
                    options.FlipVertical, options.Limits, cancellationToken);
                current.Dispose();
                current = next;
            }

            if (options.Resize is { } size)
            {
                var source = new SKRect(0, 0, current.Width, current.Height);
                var width = size.Width;
                var height = size.Height;
                if (options.ResizeMode == ImageResizeMode.Contain)
                {
                    var scale = Math.Min((double)width / current.Width, (double)height / current.Height);
                    width = Math.Max(1, (int)Math.Round(current.Width * scale));
                    height = Math.Max(1, (int)Math.Round(current.Height * scale));
                }
                else if (options.ResizeMode == ImageResizeMode.Cover)
                {
                    var scale = Math.Max((double)width / current.Width, (double)height / current.Height);
                    var sourceWidth = (float)(width / scale);
                    var sourceHeight = (float)(height / scale);
                    source = SKRect.Create((current.Width - sourceWidth) / 2, (current.Height - sourceHeight) / 2,
                        sourceWidth, sourceHeight);
                }

                var next = Draw(current, source, width, height, options.Limits, SKFilterMode.Linear, cancellationToken);
                current.Dispose();
                current = next;
            }

            await ImageCodec.EncodeAsync(current, output, options.Format, options.Quality,
                options.JpegBackground, cancellationToken).ConfigureAwait(false);
            return new(current.Width, current.Height, options.Format);
        }
        finally { current.Dispose(); }
    }

    private static SKBitmap Draw(
        SKBitmap source, SKRect sourceRect, int width, int height, ImageLimits limits,
        SKFilterMode filter, CancellationToken cancellationToken)
    {
        var result = ImageCodec.CreateBitmap(width, height, limits);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            using var image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, sourceRect, new SKRect(0, 0, width, height), new SKSamplingOptions(filter));
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
