using SkiaSharp;

namespace MS.Microservice.Documents.Rendering;

internal static class RenderImageValidator
{
    internal static void Validate(byte[] encoded, string expectedMime, long maximumPixels)
    {
        using var input = new SKMemoryStream(encoded);
        using var codec = SKCodec.Create(input);
        if (codec is null || codec.Info.Width < 1 || codec.Info.Height < 1
            || (long)codec.Info.Width * codec.Info.Height > maximumPixels || codec.FrameCount > 1)
            throw new InvalidDataException("An image asset is invalid, animated, or exceeds the pixel limit.");
        if (expectedMime != codec.EncodedFormat switch
            { SKEncodedImageFormat.Png => "image/png", SKEncodedImageFormat.Jpeg => "image/jpeg", SKEncodedImageFormat.Webp => "image/webp", _ => string.Empty })
            throw new InvalidDataException("An image asset does not match its MIME type.");
    }
}
