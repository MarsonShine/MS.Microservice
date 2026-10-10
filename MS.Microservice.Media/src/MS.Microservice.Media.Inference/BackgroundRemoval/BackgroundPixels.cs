using System.Numerics;
using SkiaSharp;

namespace MS.Microservice.Media.Inference.BackgroundRemoval;

internal static class BackgroundPixels
{
    internal static float[] CreateTensor(SKBitmap source, int size, CancellationToken token)
    {
        var plane = size * size;
        var tensor = new float[3 * plane];
        for (var y = 0; y < size; y++)
        {
            token.ThrowIfCancellationRequested();
            var (top, bottom, dy) = Coordinates(y, size, source.Height);
            for (var x = 0; x < size; x++)
            {
                var (left, right, dx) = Coordinates(x, size, source.Width);
                var upper = Vector3.Lerp(WhiteComposite(source.GetPixel(left, top)), WhiteComposite(source.GetPixel(right, top)), dx);
                var lower = Vector3.Lerp(WhiteComposite(source.GetPixel(left, bottom)), WhiteComposite(source.GetPixel(right, bottom)), dx);
                var rgb = Vector3.Lerp(upper, lower, dy);
                var index = y * size + x;
                tensor[index] = (rgb.X - .485f) / .229f;
                tensor[plane + index] = (rgb.Y - .456f) / .224f;
                tensor[2 * plane + index] = (rgb.Z - .406f) / .225f;
            }
        }
        return tensor;
    }

    internal static float[] Probabilities(ReadOnlySpan<float> logits, int size, CancellationToken token)
    {
        if (logits.Length != size * size) throw new InvalidDataException("The model mask shape is invalid.");
        var mask = new float[logits.Length];
        for (var y = 0; y < size; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < size; x++)
            {
                var index = y * size + x;
                var value = logits[index];
                if (!float.IsFinite(value)) throw new InvalidDataException("The model returned non-finite mask logits.");
                var exponential = MathF.Exp(value >= 0 ? -value : value);
                mask[index] = value >= 0 ? 1 / (1 + exponential) : exponential / (1 + exponential);
            }
        }
        return mask;
    }

    internal static (int NonTransparent, double MeanAlpha) Compose(SKBitmap source, SKBitmap output, SKBitmap? softMask,
        ReadOnlySpan<float> probabilities, int size, CancellationToken token)
    {
        if (probabilities.Length != size * size) throw new InvalidDataException("The estimated mask shape is invalid.");
        var visible = 0;
        long alphaSum = 0;
        for (var y = 0; y < source.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            var (top, bottom, dy) = Coordinates(y, source.Height, size);
            for (var x = 0; x < source.Width; x++)
            {
                var (left, right, dx) = Coordinates(x, source.Width, size);
                var upper = probabilities[top * size + left] * (1 - dx) + probabilities[top * size + right] * dx;
                var lower = probabilities[bottom * size + left] * (1 - dx) + probabilities[bottom * size + right] * dx;
                var probability = upper * (1 - dy) + lower * dy;
                var pixel = source.GetPixel(x, y);
                var alpha = (byte)Math.Clamp((int)MathF.Round(pixel.Alpha * probability), 0, 255);
                output.SetPixel(x, y, pixel.WithAlpha(alpha));
                softMask?.SetPixel(x, y, new SKColor(alpha, alpha, alpha, 255));
                if (alpha > 0) visible++;
                alphaSum += alpha;
            }
        }
        return (visible, (double)alphaSum / (255L * source.Width * source.Height));
    }

    internal static SKBitmap CreateStraightBitmap(int width, int height)
    {
        var bitmap = new SKBitmap();
        if (bitmap.TryAllocPixels(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul))) return bitmap;
        bitmap.Dispose();
        throw new OutOfMemoryException("Cannot allocate background removal output pixels.");
    }

    private static Vector3 WhiteComposite(SKColor color)
    {
        var alpha = color.Alpha / 255f;
        return new Vector3(color.Red, color.Green, color.Blue) / 255 * alpha + Vector3.One * (1 - alpha);
    }

    private static (int Low, int High, float Fraction) Coordinates(int destination, int targetLength, int sourceLength)
    {
        var value = Math.Clamp((destination + .5) * sourceLength / targetLength - .5, 0, sourceLength - 1);
        var low = (int)value;
        return (low, Math.Min(sourceLength - 1, low + 1), (float)(value - low));
    }
}
