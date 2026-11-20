using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using PDFtoImage;
using SkiaSharp;

namespace MS.Microservice.Documents.Pdf;

public sealed partial class PdfProcessor
{
    public async Task<PdfRenderedPage> RenderPageAsync(
        Stream input, int page, Stream output, int dpi = 150, PdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        Destination(output, input);
        await foreach (var rendered in RenderPagesAsync(input, [page], dpi, options, cancellationToken).ConfigureAwait(false))
        {
            await output.WriteAsync(rendered.Png, cancellationToken).ConfigureAwait(false);
            return rendered;
        }
        throw new InvalidOperationException("No PDF page was rendered.");
    }

    public async IAsyncEnumerable<PdfRenderedPage> RenderPagesAsync(
        Stream input, IReadOnlyList<int>? pages = null, int dpi = 150, PdfOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options = Settings(options);
        if (dpi is < 1 or > 1200) throw new ArgumentOutOfRangeException(nameof(dpi));
        if (!SupportsPdfiumRuntime)
            throw new PlatformNotSupportedException("PDF rasterization supports Windows, Linux and macOS desktop runtimes.");
        var bytes = await ReadAsync(input, options.Limits, cancellationToken).ConfigureAwait(false);
        string? password;
        using (var content = OpenContent(bytes, options.Password, out password)) CheckPages(content.NumberOfPages, options.Limits);
        using var data = new MemoryStream(bytes, writable: false);
        CheckPages(Conversion.GetPageCount(data, leaveOpen: true, password: password), options.Limits);
        data.Position = 0;
        var sizes = Conversion.GetPageSizes(data, leaveOpen: true, password: password);
        var selected = Selection(pages, sizes.Count, options.Limits);
        foreach (var page in selected)
        {
            var size = sizes[page - 1];
            // PDFtoImage 5.4 truncates positive scaled sizes when allocating its raster.
            CheckRaster(MathF.Floor(size.Width * (dpi / 72f)), MathF.Floor(size.Height * (dpi / 72f)), options.Limits);
        }
        data.Position = 0;
        var position = 0;
        await foreach (var bitmap in Conversion.ToImagesAsync(data, selected.Select(page => page - 1), leaveOpen: true,
            password: password, options: new RenderOptions(Dpi: dpi, WithAnnotations: true), cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            using (bitmap)
            {
                CheckRaster(bitmap.Width, bitmap.Height, options.Limits);
                using var image = SKImage.FromBitmap(bitmap);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidOperationException("PNG encoding failed.");
                if (png.Size > options.Limits.MaximumOutputBytes) throw new InvalidDataException("PNG exceeds the output byte limit.");
                cancellationToken.ThrowIfCancellationRequested();
                yield return new(selected[position++], bitmap.Width, bitmap.Height, png.ToArray());
            }
        }
    }

    public async Task AddOverlayAsync(
        Stream input, Stream pngOverlay, Stream output, PdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        Destination(output, input, pngOverlay);
        if (ReferenceEquals(input, pngOverlay)) throw new ArgumentException("PDF and PNG must be different streams.");
        options = Settings(options);
        using var source = new OwnedPdf(await ReadAsync(input, options.Limits, cancellationToken).ConfigureAwait(false), options, PdfDocumentOpenMode.Modify);
        var imageBytes = await ReadAsync(pngOverlay, options.Limits, cancellationToken).ConfigureAwait(false);
        using var skData = SKData.CreateCopy(imageBytes);
        using var codec = SKCodec.Create(skData) ?? throw new InvalidDataException("Invalid PNG overlay.");
        if (codec.EncodedFormat != SKEncodedImageFormat.Png || codec.FrameCount > 1) throw new InvalidDataException("Use a static PNG overlay.");
        CheckRaster(codec.Info.Width, codec.Info.Height, options.Limits);
        using var imageStream = new MemoryStream(imageBytes, writable: false);
        using var overlay = XImage.FromStream(imageStream);
        foreach (var page in source.Document.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var box = page.CropBox;
            if (box.IsZero) box = page.MediaBox;
            var width = box.Width;
            var height = box.Height;
            using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            graphics.TranslateTransform(box.X1, page.Height.Point - box.Y2);
            switch ((page.Rotate % 360 + 360) % 360)
            {
                case 90: graphics.TranslateTransform(0, height); graphics.RotateTransform(-90); (width, height) = (height, width); break;
                case 180: graphics.TranslateTransform(width, height); graphics.RotateTransform(180); break;
                case 270: graphics.TranslateTransform(width, 0); graphics.RotateTransform(90); (width, height) = (height, width); break;
            }
            graphics.DrawImage(overlay, 0, 0, width, height);
        }
        await output.WriteAsync(Save(source.Document, options, source.IsEncrypted, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static void CheckRaster(double width, double height, PdfLimits limits)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 1 || height < 1
            || width > limits.MaximumRasterDimension || height > limits.MaximumRasterDimension
            || width * height > limits.MaximumRasterPixels || width * height > int.MaxValue / 4)
            throw new InvalidDataException("PDF raster dimensions exceed the configured limits.");
    }

    [SupportedOSPlatformGuard("Windows")]
    [SupportedOSPlatformGuard("Linux")]
    [SupportedOSPlatformGuard("macOS")]
    private static bool SupportsPdfiumRuntime => OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
}
