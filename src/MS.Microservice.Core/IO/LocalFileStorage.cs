namespace MS.Microservice.Core.IO;

/// <summary>Saves streams under generated names in a caller-selected local directory.</summary>
public sealed class LocalFileStorage : IFileStorage
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
        var key = StoredFileKey.Create(extension);
        await WriteNewAsync(key, content, cancellationToken).ConfigureAwait(false);
        return key;
    }

    public async Task WriteNewAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        StoredFileKey.Validate(key);
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead)
        {
            throw new ArgumentException("The content stream must be readable.", nameof(content));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var outputPath = Path.Combine(_directory, key);
        if (File.Exists(outputPath) || Directory.Exists(outputPath)) throw new IOException("The stored file already exists.");
        Directory.CreateDirectory(_directory);
        var id = Guid.NewGuid().ToString("N");
        var temporaryPath = Path.Combine(_directory, "." + id + ".tmp");

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
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        StoredFileKey.Validate(key);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Stream stream = new FileStream(Path.Combine(_directory, key), FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult(stream);
        }
        catch (DirectoryNotFoundException error)
        { throw new FileNotFoundException("The stored file does not exist.", key, error); }
    }

    public Task<StoredFileMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        StoredFileKey.Validate(key);
        cancellationToken.ThrowIfCancellationRequested();
        var file = new FileInfo(Path.Combine(_directory, key));
        return Task.FromResult(file.Exists ? new StoredFileMetadata(key, file.Length, new(file.LastWriteTimeUtc)) : null);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        StoredFileKey.Validate(key);
        cancellationToken.ThrowIfCancellationRequested();
        try { File.Delete(Path.Combine(_directory, key)); }
        catch (DirectoryNotFoundException) { }
        return Task.CompletedTask;
    }
}
