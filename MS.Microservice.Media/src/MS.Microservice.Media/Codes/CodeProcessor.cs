using MS.Microservice.Media.Images;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode.Internal;
using System.Text;

namespace MS.Microservice.Media.Codes;

public sealed class CodeProcessor
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<ImageProcessingResult> GenerateAsync(string text, Stream output,
        CodeGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite) throw new ArgumentException("Output must be writable.", nameof(output));
        options ??= new CodeGenerationOptions();
        ImageCodec.ValidateLimits(options.Limits);
        ImageCodec.CheckDimensions(options.Width, options.Height, options.Limits);
        ArgumentNullException.ThrowIfNull(options.Foreground);
        ArgumentNullException.ThrowIfNull(options.Background);
        if (!Enum.IsDefined(options.QrErrorCorrection) || options.MaximumTextCharacters < 1 ||
            text.Length > options.MaximumTextCharacters || options.Margin < 0 ||
            options.Margin >= Math.Min(options.Width, options.Height) / 2 ||
            options.Foreground.Alpha != 255 || options.Background.Alpha != 255 ||
            Brightness(options.Foreground) >= Brightness(options.Background))
            throw new ArgumentException("Choose bounded content, margin and opaque dark-on-light colors.", nameof(options));
        try { StrictUtf8.GetByteCount(text); }
        catch (EncoderFallbackException ex)
        {
            throw new ArgumentException("Code content must contain well-formed UTF-16.", nameof(text), ex);
        }
        var encoding = new EncodingOptions { Width = options.Width, Height = options.Height, PureBarcode = true };
        if (options.Margin is { } margin) encoding.Margin = margin;
        else if (options.Format == CodeFormat.DataMatrix) encoding.Margin = 1;
        if (options.Format == CodeFormat.Qr)
        {
            encoding.Hints[EncodeHintType.CHARACTER_SET] = "UTF-8";
            encoding.Hints[EncodeHintType.ERROR_CORRECTION] = options.QrErrorCorrection switch
            {
                QrErrorCorrection.Low => ErrorCorrectionLevel.L,
                QrErrorCorrection.Medium => ErrorCorrectionLevel.M,
                QrErrorCorrection.Quartile => ErrorCorrectionLevel.Q,
                _ => ErrorCorrectionLevel.H
            };
        }
        var writer = new BarcodeWriterGeneric { Format = ToBarcodeFormat(options.Format), Options = encoding };
        cancellationToken.ThrowIfCancellationRequested();
        var matrix = writer.Encode(text);
        cancellationToken.ThrowIfCancellationRequested();
        ImageCodec.CheckDimensions(matrix.Width, matrix.Height, options.Limits);
        if (matrix.Width > options.Width || matrix.Height > options.Height)
            throw new ArgumentException("The code cannot fit the requested dimensions without clipping or resampling.", nameof(options));

        using var bitmap = ImageCodec.CreateBitmap(options.Width, options.Height, options.Limits);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = Color(options.Foreground), IsAntialias = false })
        {
            canvas.Clear(Color(options.Background));
            var left = (options.Width - matrix.Width) / 2;
            var top = (options.Height - matrix.Height) / 2;
            for (var y = 0; y < matrix.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < matrix.Width;)
                {
                    if (!matrix[x, y]) { x++; continue; }
                    var start = x++;
                    while (x < matrix.Width && matrix[x, y]) x++;
                    canvas.DrawRect(left + start, top + y, x - start, 1, paint);
                }
            }
        }
        await ImageCodec.EncodeAsync(bitmap, output, ImageOutputFormat.Png, 100,
            options.Background, cancellationToken).ConfigureAwait(false);
        return new(options.Width, options.Height, ImageOutputFormat.Png);
    }

    public async Task<IReadOnlyList<DecodedCode>> DecodeAsync(Stream input,
        CodeReadingOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new CodeReadingOptions();
        var formats = (options.Formats ?? Enum.GetValues<CodeFormat>()).Select(ToBarcodeFormat).Distinct().ToList();
        if (formats.Count == 0) throw new ArgumentException("Choose at least one code format.", nameof(options));
        using var bitmap = await ImageCodec.DecodeAsync(input, options.Limits, cancellationToken).ConfigureAwait(false);
        var rgb = new byte[checked(bitmap.Width * bitmap.Height * 3)];
        var pixels = bitmap.GetPixelSpan();
        for (var y = 0; y < bitmap.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < bitmap.Width; x++)
            {
                var source = y * bitmap.RowBytes + x * 4;
                var target = (y * bitmap.Width + x) * 3;
                var white = 255 - pixels[source + 3];
                // ImageCodec guarantees RGBA premultiplied pixels. Flatten alpha onto white.
                rgb[target] = (byte)Math.Min(255, pixels[source] + white);
                rgb[target + 1] = (byte)Math.Min(255, pixels[source + 1] + white);
                rgb[target + 2] = (byte)Math.Min(255, pixels[source + 2] + white);
            }
        }
        var reader = new BarcodeReaderGeneric(new FrameReader(), null, null)
        {
            AutoRotate = false,
            Options = new DecodingOptions
            {
                PossibleFormats = formats, TryHarder = options.TryHarder, TryInverted = options.TryInverted
            }
        };
        // Own rotation so points stay in the displayed frame; ZXing's outer AutoRotate does not remap them.
        reader.Options.Hints[DecodeHintType.TRY_HARDER_WITHOUT_ROTATION] = true;
        LuminanceSource luminance = new RGBLuminanceSource(rgb, bitmap.Width, bitmap.Height,
            RGBLuminanceSource.BitmapFormat.RGB24);
        Result[]? results = null;
        var turns = 0;
        var attempts = options.AutoRotate ? 4 : 1;
        for (; turns < attempts; turns++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results = reader.DecodeMultiple(luminance);
            cancellationToken.ThrowIfCancellationRequested();
            if (results is { Length: > 0 }) break;
            if (turns + 1 < attempts) luminance = luminance.rotateCounterClockwise();
        }
        return (results ?? []).Select(result => new DecodedCode(result.Text, FromBarcodeFormat(result.BarcodeFormat),
            Array.AsReadOnly((result.ResultPoints ?? []).Where(point => point is not null)
                .Select(point => RestorePoint(point, turns, bitmap.Width, bitmap.Height)).ToArray()))).ToArray();
    }

    private static CodePoint RestorePoint(ResultPoint point, int counterClockwiseTurns, int width, int height)
        => counterClockwiseTurns switch
        {
            1 => new(width - point.Y - 1, point.X),
            2 => new(width - point.X - 1, height - point.Y - 1),
            3 => new(point.Y, height - point.X - 1),
            _ => new(point.X, point.Y)
        };

    // ZXing 0.16.11 PDF417 returns detector-rotated points; normalize before multi-reader recursive crops.
    private sealed class FrameReader : Reader
    {
        private readonly MultiFormatReader _reader = new();
        public Result decode(BinaryBitmap image) => decode(image, null!);
        public Result decode(BinaryBitmap image, IDictionary<DecodeHintType, object> hints)
        {
            var result = _reader.decode(image, hints);
            if (result?.BarcodeFormat == BarcodeFormat.PDF_417 && result.ResultPoints is { } points &&
                result.ResultMetadata?.TryGetValue(ResultMetadataType.ORIENTATION, out var orientation) == true &&
                orientation is int degrees)
            {
                for (var index = 0; index < points.Length; index++)
                {
                    if (points[index] is not { } point) continue;
                    var displayed = RestorePoint(point, degrees / 90, image.Width, image.Height);
                    points[index] = new ResultPoint(displayed.X, displayed.Y);
                }
            }
            return result!;
        }
        public void reset() => _reader.reset();
    }

    private static SKColor Color(ImageColor color) => new(color.Red, color.Green, color.Blue);
    private static int Brightness(ImageColor color) => color.Red * 299 + color.Green * 587 + color.Blue * 114;

    private static BarcodeFormat ToBarcodeFormat(CodeFormat format) => format switch
    {
        CodeFormat.Qr => BarcodeFormat.QR_CODE, CodeFormat.Code128 => BarcodeFormat.CODE_128,
        CodeFormat.Ean13 => BarcodeFormat.EAN_13, CodeFormat.Ean8 => BarcodeFormat.EAN_8,
        CodeFormat.UpcA => BarcodeFormat.UPC_A, CodeFormat.Code39 => BarcodeFormat.CODE_39,
        CodeFormat.DataMatrix => BarcodeFormat.DATA_MATRIX, CodeFormat.Pdf417 => BarcodeFormat.PDF_417,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private static CodeFormat FromBarcodeFormat(BarcodeFormat format) => format switch
    {
        BarcodeFormat.QR_CODE => CodeFormat.Qr, BarcodeFormat.CODE_128 => CodeFormat.Code128,
        BarcodeFormat.EAN_13 => CodeFormat.Ean13, BarcodeFormat.EAN_8 => CodeFormat.Ean8,
        BarcodeFormat.UPC_A => CodeFormat.UpcA, BarcodeFormat.CODE_39 => CodeFormat.Code39,
        BarcodeFormat.DATA_MATRIX => CodeFormat.DataMatrix, BarcodeFormat.PDF_417 => CodeFormat.Pdf417,
        _ => throw new InvalidDataException("The reader returned an unsupported code format.")
    };
}
