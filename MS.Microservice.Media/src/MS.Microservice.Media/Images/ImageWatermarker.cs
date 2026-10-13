using System.Buffers;
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace MS.Microservice.Media.Images;

public sealed class ImageWatermarker
{
    public async Task<ImageProcessingResult> AddTextAsync(
        Stream input, Stream destination, TextWatermark text, WatermarkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new();
        Validate(destination, options, input);
        using var image = await ImageCodec.DecodeAsync(input, options.Limits, cancellationToken).ConfigureAwait(false);
        using var stamp = TextStamp(text, options.Limits, cancellationToken);
        Draw(image, stamp, options, cancellationToken);
        await ImageCodec.EncodeAsync(image, destination, options.Format, options.Quality, options.JpegBackground, cancellationToken).ConfigureAwait(false);
        return new(image.Width, image.Height, options.Format);
    }

    public async Task<ImageProcessingResult> AddLogoAsync(
        Stream input, Stream logo, Stream destination, ImageSize? logoSize = null,
        WatermarkOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        Validate(destination, options, input, logo);
        if (ReferenceEquals(input, logo)) throw new ArgumentException("Image and logo must be different streams.", nameof(logo));
        using var image = await ImageCodec.DecodeAsync(input, options.Limits, cancellationToken).ConfigureAwait(false);
        using var decoded = await ImageCodec.DecodeAsync(logo, options.Limits, cancellationToken).ConfigureAwait(false);
        using var resized = ResizeLogo(decoded, logoSize, options.Limits, cancellationToken);
        Draw(image, resized ?? decoded, options, cancellationToken);
        await ImageCodec.EncodeAsync(image, destination, options.Format, options.Quality, options.JpegBackground, cancellationToken).ConfigureAwait(false);
        return new(image.Width, image.Height, options.Format);
    }

    public async Task RenderTextOverlayAsync(
        int width, int height, Stream destination, TextWatermark text, WatermarkPlacement? placement = null,
        CancellationToken cancellationToken = default)
    {
        placement ??= new();
        Validate(destination, placement);
        using var overlay = ImageCodec.CreateBitmap(width, height, placement.Limits);
        using var stamp = TextStamp(text, placement.Limits, cancellationToken);
        using (var canvas = new SKCanvas(overlay)) canvas.Clear(SKColors.Transparent);
        Draw(overlay, stamp, placement, cancellationToken);
        await ImageCodec.EncodeAsync(overlay, destination, ImageOutputFormat.Png, 100, new(255, 255, 255), cancellationToken).ConfigureAwait(false);
    }

    public async Task RenderLogoOverlayAsync(
        int width, int height, Stream logo, Stream destination, ImageSize? logoSize = null,
        WatermarkPlacement? placement = null, CancellationToken cancellationToken = default)
    {
        placement ??= new();
        Validate(destination, placement, logo);
        using var overlay = ImageCodec.CreateBitmap(width, height, placement.Limits);
        using var decoded = await ImageCodec.DecodeAsync(logo, placement.Limits, cancellationToken).ConfigureAwait(false);
        using var resized = ResizeLogo(decoded, logoSize, placement.Limits, cancellationToken);
        using (var canvas = new SKCanvas(overlay)) canvas.Clear(SKColors.Transparent);
        Draw(overlay, resized ?? decoded, placement, cancellationToken);
        await ImageCodec.EncodeAsync(overlay, destination, ImageOutputFormat.Png, 100, new(255, 255, 255), cancellationToken).ConfigureAwait(false);
    }

    private static void Validate(Stream destination, WatermarkPlacement placement, params Stream[] inputs)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ImageCodec.ValidateLimits(placement.Limits);
        foreach (var input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (ReferenceEquals(input, destination)) throw new ArgumentException("Input and output must be different streams.");
        }

        if (!destination.CanWrite) throw new ArgumentException("Output must be writable.", nameof(destination));
        if (!Enum.IsDefined(placement.Anchor) || placement.MarginX < 0 || placement.MarginY < 0
            || !double.IsFinite(placement.Opacity) || placement.Opacity is < 0 or > 1
            || !float.IsFinite(placement.RotationDegrees) || placement.TileGap < 0 || placement.MaximumTiles < 1)
            throw new ArgumentException("Invalid watermark placement, opacity, angle or tile budget.", nameof(placement));
        if (placement is WatermarkOptions output && (!Enum.IsDefined(output.Format) || output.Quality is < 0 or > 100
            || output.JpegBackground is null || output.JpegBackground.Alpha != 255))
            throw new ArgumentException("Invalid watermark output encoding.", nameof(placement));
    }

    private static SKBitmap TextStamp(TextWatermark text, ImageLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(text.Color);
        if (string.IsNullOrWhiteSpace(text.Text) || text.Text.Length > 4096
            || !float.IsFinite(text.FontSize) || text.FontSize <= 0 || text.FontSize > limits.MaximumDimension)
            throw new ArgumentException("Provide visible single-line text of at most 4096 UTF-16 units and a positive bounded font size.", nameof(text));
        var remaining = text.Text.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var count) != OperationStatus.Done || Rune.IsControl(rune))
                throw new ArgumentException("Text must contain valid Unicode without control characters.", nameof(text));
            remaining = remaining[count..];
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var loaded = text.FontFile is null ? null
            : SKTypeface.FromFile(text.FontFile) ?? throw new ArgumentException("Cannot load the specified font file.", nameof(text));
        var typeface = loaded ?? SKTypeface.Default;
        using var font = new SKFont(typeface, text.FontSize);
        if (!font.ContainsGlyphs(text.Text)) throw new NotSupportedException("The selected font does not cover all watermark characters.");
        using var shaper = new SKShaper(typeface);
        var shaped = shaper.Shape(text.Text, font);
        using var builder = new SKTextBlobBuilder();
        var run = builder.AllocateRawPositionedRun(font, shaped.Codepoints.Length);
        for (var index = 0; index < shaped.Codepoints.Length; index++)
        {
            if (shaped.Codepoints[index] == 0) throw new NotSupportedException("Shaping produced a missing glyph.");
            run.Glyphs[index] = checked((ushort)shaped.Codepoints[index]);
            run.Positions[index] = shaped.Points[index];
        }

        // Blob bounds include conservative typeface extents; size the stamp from the shaped glyphs instead.
        var glyphBounds = new SKRect[shaped.Codepoints.Length];
        font.GetGlyphWidths(run.Glyphs, Span<float>.Empty, glyphBounds);
        var bounds = SKRect.Empty;
        for (var index = 0; index < glyphBounds.Length; index++)
        {
            var glyph = glyphBounds[index];
            if (glyph.IsEmpty) continue;
            glyph.Offset(shaped.Points[index]);
            bounds = bounds.IsEmpty ? glyph : SKRect.Union(bounds, glyph);
        }
        if (bounds.IsEmpty) throw new ArgumentException("Text has no drawable glyphs.", nameof(text));
        using var blob = builder.Build() ?? throw new ArgumentException("Text has no drawable glyphs.", nameof(text));
        var width = BoundedDimension(bounds.Width + 4, limits);
        var height = BoundedDimension(bounds.Height + 4, limits);
        var stamp = ImageCodec.CreateBitmap(width, height, limits);
        try
        {
            using var canvas = new SKCanvas(stamp);
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { IsAntialias = true, Color = new(text.Color.Red, text.Color.Green, text.Color.Blue, text.Color.Alpha) };
            canvas.DrawText(blob, 2 - bounds.Left, 2 - bounds.Top, paint);
            cancellationToken.ThrowIfCancellationRequested();
            return stamp;
        }
        catch { stamp.Dispose(); throw; }
    }

    private static SKBitmap? ResizeLogo(SKBitmap logo, ImageSize? size, ImageLimits limits, CancellationToken cancellationToken)
    {
        if (size is null) return null;
        ImageCodec.CheckDimensions(size.Width, size.Height, limits);
        var scale = Math.Min((double)size.Width / logo.Width, (double)size.Height / logo.Height);
        var resized = ImageCodec.CreateBitmap(Math.Max(1, (int)Math.Round(logo.Width * scale)),
            Math.Max(1, (int)Math.Round(logo.Height * scale)), limits);
        try
        {
            using var canvas = new SKCanvas(resized);
            canvas.Clear(SKColors.Transparent);
            using var image = SKImage.FromBitmap(logo);
            canvas.DrawImage(image, new SKRect(0, 0, resized.Width, resized.Height), new SKSamplingOptions(SKFilterMode.Linear));
            cancellationToken.ThrowIfCancellationRequested();
            return resized;
        }
        catch { resized.Dispose(); throw; }
    }

    private static void Draw(SKBitmap destination, SKBitmap stamp, WatermarkPlacement placement, CancellationToken cancellationToken)
    {
        var angle = (placement.RotationDegrees % 360 + 360) % 360;
        var radians = angle * Math.PI / 180;
        var cosine = Math.Abs(Math.Cos(radians));
        var sine = Math.Abs(Math.Sin(radians));
        var width = BoundedDimension(angle is 90 or 270 ? stamp.Height : angle is 0 or 180 ? stamp.Width
            : stamp.Width * cosine + stamp.Height * sine, placement.Limits);
        var height = BoundedDimension(angle is 90 or 270 ? stamp.Width : angle is 0 or 180 ? stamp.Height
            : stamp.Width * sine + stamp.Height * cosine, placement.Limits);
        var left = placement.MarginX;
        var top = placement.MarginY;
        var availableWidth = (long)destination.Width - 2L * left;
        var availableHeight = (long)destination.Height - 2L * top;
        if (availableWidth < 1 || availableHeight < 1
            || !placement.Tile && (width > availableWidth || height > availableHeight))
            throw new ArgumentException("The rotated watermark must fit inside the image margins.", nameof(placement));
        var column = (int)placement.Anchor % 3;
        var row = (int)placement.Anchor / 3;
        double x = left + (availableWidth - width) * column / 2d;
        double y = top + (availableHeight - height) * row / 2d;
        var stepX = (long)width + placement.TileGap;
        var stepY = (long)height + placement.TileGap;
        var columns = 1L;
        var rows = 1L;
        if (placement.Tile)
        {
            var phaseX = ((x - left) % stepX + stepX) % stepX;
            var phaseY = ((y - top) % stepY + stepY) % stepY;
            x = left + phaseX - (phaseX > placement.TileGap ? stepX : 0);
            y = top + phaseY - (phaseY > placement.TileGap ? stepY : 0);
            columns = (long)Math.Ceiling((destination.Width - left - x) / stepX);
            rows = (long)Math.Ceiling((destination.Height - top - y) / stepY);
            if (columns > placement.MaximumTiles / rows)
                throw new ArgumentException("Watermark tiling exceeds the configured tile budget.", nameof(placement));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var canvas = new SKCanvas(destination);
        canvas.ClipRect(new SKRect(left, top, destination.Width - left, destination.Height - top));
        using var image = SKImage.FromBitmap(stamp);
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(placement.Opacity * 255)), IsAntialias = true };
        for (var r = 0L; r < rows; r++)
        for (var c = 0L; c < columns; c++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            canvas.Save();
            canvas.Translate((float)(x + c * stepX + width / 2d), (float)(y + r * stepY + height / 2d));
            canvas.RotateDegrees(angle);
            canvas.DrawImage(image, -stamp.Width / 2f, -stamp.Height / 2f, new SKSamplingOptions(SKFilterMode.Linear), paint);
            canvas.Restore();
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static int BoundedDimension(double dimension, ImageLimits limits)
    {
        if (!double.IsFinite(dimension) || dimension <= 0 || dimension > limits.MaximumDimension)
            throw new InvalidDataException("Watermark dimensions exceed image limits.");
        return (int)Math.Ceiling(dimension);
    }
}
