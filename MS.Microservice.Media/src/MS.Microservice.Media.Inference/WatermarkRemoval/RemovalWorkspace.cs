namespace MS.Microservice.Media.Inference.WatermarkRemoval;

internal sealed class RemovalWorkspace
{
    internal string DirectoryPath { get; }
    internal string ImagePath => Path.Combine(DirectoryPath, "original.bin");
    internal string MaskPath => Path.Combine(DirectoryPath, "mask.bin");

    internal RemovalWorkspace(string parent)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        DirectoryPath = Path.GetFullPath(Path.Combine(root, "ms-watermark-" + Guid.NewGuid().ToString("N")));
        if (!string.Equals(Path.GetDirectoryName(DirectoryPath), root,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("The temporary workspace must be a direct child of the configured directory.");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(DirectoryPath);
        else Directory.CreateDirectory(DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    internal void Delete()
    {
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
    }

    internal static async Task SnapshotAsync(Stream source, string destination, int maximumBytes, CancellationToken cancellationToken)
    {
        if (!source.CanRead) throw new ArgumentException("The input must be readable.", nameof(source));
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maximumBytes - total + 1)), cancellationToken).ConfigureAwait(false)) != 0)
        {
            total += read;
            if (total > maximumBytes) throw new InvalidDataException("The encoded image or mask exceeds its configured byte limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        if (total == 0) throw new InvalidDataException("The encoded image or mask is empty.");
        cancellationToken.ThrowIfCancellationRequested();
    }
}
