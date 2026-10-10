namespace MS.Microservice.Core.IO;

/// <summary>Saves streams under generated names in a caller-selected local directory.</summary>
public sealed class LocalFileStorage
{
    private readonly string _directory;

    /// <param name="directory">A trusted storage directory, resolved when this instance is created.</param>
    public LocalFileStorage(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    /// <summary>
    /// Copies from the current input position and returns the generated file name after publication.
    /// Leaves the input open. Failed or cancelled copies do not publish an output file.
    /// </summary>
    public async Task<string> SaveAsync(
        Stream content,
        string extension,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead)
        {
            throw new ArgumentException("The content stream must be readable.", nameof(content));
        }

        var suffix = NormalizeExtension(extension);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_directory);
        var id = Guid.NewGuid().ToString("N");
        var fileName = id + suffix;
        var temporaryPath = Path.Combine(_directory, "." + id + ".tmp");
        var outputPath = Path.Combine(_directory, fileName);

        // Open outside the cleanup scope: a failed CreateNew never grants ownership of that path.
        var output = new FileStream(
            temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        try
        {
            await using (output)
            {
                await content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, outputPath, overwrite: false);
            return fileName;
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)
            || extension.Length is < 2 or > 10
            || extension[0] != '.'
            || extension.AsSpan(1).ContainsAnyExcept(
                "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"))
        {
            throw new ArgumentException("Use a dot followed by 1 to 9 ASCII letters or digits.", nameof(extension));
        }

        return extension.ToLowerInvariant();
    }
}
