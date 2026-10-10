namespace MS.Microservice.Core.IO;

/// <summary>Immutable file writes and retrieval by canonical generated keys; callers own authorization.</summary>
public interface IFileStorage
{
    /// <summary>Writes the entire remaining stream under a caller-created key, without replacing an existing file.</summary>
    Task WriteNewAsync(string key, Stream content, CancellationToken cancellationToken = default);
    /// <summary>Returns a stream owned by the caller; a missing file throws FileNotFoundException.</summary>
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);
    Task<StoredFileMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken = default);
    /// <summary>Deletes the current file; absence succeeds. Cancellation does not undo a completed deletion.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public sealed record StoredFileMetadata(string Key, long Length, DateTimeOffset LastModifiedUtc);
