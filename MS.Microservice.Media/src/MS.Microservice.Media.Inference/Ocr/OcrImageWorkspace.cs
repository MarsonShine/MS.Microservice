using MS.Microservice.Media.Images;
using MS.Microservice.Media.Inference.WatermarkRemoval;

namespace MS.Microservice.Media.Inference.Ocr;

internal sealed class OcrImageWorkspace(RemovalWorkspace workspace, int width, int height)
{
    internal string ImagePath => Path.Combine(workspace.DirectoryPath, "normalized.png");
    internal int Width => width;
    internal int Height => height;

    internal static async Task<OcrImageWorkspace> CreateAsync(Stream input, OcrOptions options, CancellationToken cancellationToken)
    {
        using var image = await ImageCodec.DecodeAsync(input, options.Limits, cancellationToken).ConfigureAwait(false);
        var result = new OcrImageWorkspace(new RemovalWorkspace(options.TemporaryDirectory), image.Width, image.Height);
        try
        {
            await using var output = new ScratchStream(result.ImagePath, options.MaximumIntermediateBytes);
            await ImageCodec.EncodeAsync(image, output, ImageOutputFormat.Png, 100, new(255, 255, 255), cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception error) { result.Delete(error); throw; }
    }

    internal void Delete(Exception? failure)
    {
        try { workspace.Delete(); }
        catch (Exception cleanup) when (failure is not null)
        { throw new AggregateException("Image inference failed and its temporary workspace could not be removed.", failure, cleanup); }
    }

    private sealed class ScratchStream(string path, int maximumBytes) : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true)
    {
        private void Check(int count)
        {
            if (Position > (long)maximumBytes - count)
                throw new InvalidDataException("The normalized OCR PNG exceeds the intermediate byte budget.");
        }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        { Check(count); return base.WriteAsync(buffer, offset, count, cancellationToken); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { Check(buffer.Length); return base.WriteAsync(buffer, cancellationToken); }
    }
}
