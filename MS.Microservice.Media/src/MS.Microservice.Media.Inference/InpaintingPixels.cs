using SkiaSharp;

namespace MS.Microservice.Media.Inference;

internal static class InpaintingPixels
{
    // Fixed MI-GAN pipeline: known-mask maxpool3 radius1 + Gaussian5 radius2.
    internal const int InferencePadding = 1 + 2;

    internal static (byte[] Edits, int Selected) ReadMask(SKBitmap mask, int width, int height, CancellationToken cancellationToken)
    {
        if (mask.Width != width || mask.Height != height)
            throw new ArgumentException("Mask dimensions must exactly match the oriented image.", nameof(mask));
        var edits = new byte[checked(width * height)];
        var count = 0;
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var pixel = mask.GetPixel(x, y);
                if (pixel.Alpha != 255 || pixel.Red != pixel.Green || pixel.Red != pixel.Blue || pixel.Red is not (0 or 255))
                    throw new ArgumentException("Mask pixels must be opaque grayscale: black keeps, white edits; no intermediate values.", nameof(mask));
                if (pixel.Red == 255) { edits[y * width + x] = 1; count++; }
            }
        }
        return (edits, count);
    }

    internal static byte[] CreateKnownMask(byte[] edits, int width, int height, CancellationToken cancellationToken)
    {
        var horizontal = new byte[edits.Length];
        var known = new byte[edits.Length];
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = y * width;
            var count = 0;
            for (var i = 0; i <= Math.Min(InferencePadding, width - 1); i++) count += edits[offset + i];
            for (var x = 0; x < width; x++)
            {
                if (x > 0)
                {
                    if (x - InferencePadding - 1 >= 0) count -= edits[offset + x - InferencePadding - 1];
                    if (x + InferencePadding < width) count += edits[offset + x + InferencePadding];
                }
                horizontal[offset + x] = count > 0 ? (byte)1 : (byte)0;
            }
        }
        for (var x = 0; x < width; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = 0;
            for (var i = 0; i <= Math.Min(InferencePadding, height - 1); i++) count += horizontal[i * width + x];
            for (var y = 0; y < height; y++)
            {
                if (y > 0)
                {
                    if (y - InferencePadding - 1 >= 0) count -= horizontal[(y - InferencePadding - 1) * width + x];
                    if (y + InferencePadding < height) count += horizontal[(y + InferencePadding) * width + x];
                }
                known[y * width + x] = count > 0 ? (byte)0 : (byte)255;
            }
        }
        return known;
    }

    internal static byte[] CreateRgb(SKBitmap image, CancellationToken cancellationToken)
    {
        var plane = checked(image.Width * image.Height);
        var rgb = new byte[checked(plane * 3)];
        for (var y = 0; y < image.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image.GetPixel(x, y);
                var i = y * image.Width + x;
                rgb[i] = pixel.Red; rgb[plane + i] = pixel.Green; rgb[2 * plane + i] = pixel.Blue;
            }
        }
        return rgb;
    }

    internal static int Compose(SKBitmap image, byte[] edits, ReadOnlySpan<byte> rgb, CancellationToken cancellationToken)
    {
        var plane = checked(image.Width * image.Height);
        if (rgb.Length != checked(plane * 3)) throw new InvalidDataException("The model returned an unexpected RGB buffer size.");
        var changed = 0;
        for (var y = 0; y < image.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < image.Width; x++)
            {
                var i = y * image.Width + x;
                if (edits[i] == 0) continue;
                var before = image.GetPixel(x, y);
                image.SetPixel(x, y, new SKColor(rgb[i], rgb[plane + i], rgb[2 * plane + i], before.Alpha));
                if (image.GetPixel(x, y) != before) changed++;
            }
        }
        return changed;
    }
}
