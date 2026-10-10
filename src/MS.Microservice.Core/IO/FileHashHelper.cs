using System.Security.Cryptography;

namespace MS.Microservice.Core.IO;

/// <summary>A group of distinct paths with the same length and SHA-256 digest.</summary>
public sealed record FileDuplicateGroup(long Length, string Sha256, IReadOnlyList<string> Paths);

/// <summary>Streaming file digests and duplicate discovery without loading file contents into memory.</summary>
public static class FileHashHelper
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>
    /// Computes an uppercase hexadecimal SHA-256 digest from the current position to the end.
    /// Leaves the stream open and does not restore its position. Supports non-seekable streams.
    /// </summary>
    public static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The stream must be readable.", nameof(stream));

        cancellationToken.ThrowIfCancellationRequested();
        var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexString(digest);
    }

    /// <summary>
    /// Computes the digest of a stable file. Throws if a length or modification-time change is observed.
    /// The metadata checks do not provide a filesystem snapshot.
    /// </summary>
    public static Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        return ComputeFileHashAsync(ReadSnapshot(Path.GetFullPath(path)), cancellationToken);
    }

    /// <summary>
    /// Finds same-length SHA-256 matches among explicit file paths, hashing only length groups with
    /// multiple files. Paths are normalized and deduplicated using Windows case-insensitive or other
    /// platforms' ordinal comparison; links and hard links are not resolved to physical identities.
    /// Missing files, metadata/read errors and detected changes fail without partial results.
    /// The caller must keep the files stable for the duration of the operation.
    /// </summary>
    public static async Task<IReadOnlyList<FileDuplicateGroup>> FindDuplicatesAsync(
        IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        cancellationToken.ThrowIfCancellationRequested();

        var uniquePaths = new Dictionary<string, string>(PathComparer);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            var fullPath = Path.GetFullPath(path);
            if (!uniquePaths.TryGetValue(fullPath, out var previous) || StringComparer.Ordinal.Compare(fullPath, previous) < 0)
                uniquePaths[fullPath] = fullPath;
        }

        var files = new List<FileSnapshot>(uniquePaths.Count);
        foreach (var path in uniquePaths.Values.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(ReadSnapshot(path));
        }

        var result = new List<FileDuplicateGroup>();
        foreach (var lengthGroup in files.GroupBy(file => file.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lengthGroup.Count() < 2)
                continue;

            var byDigest = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var file in lengthGroup)
            {
                var digest = await ComputeFileHashAsync(file, cancellationToken).ConfigureAwait(false);
                if (!byDigest.TryGetValue(digest, out var matches))
                    byDigest[digest] = matches = [];
                matches.Add(file.Path);
            }

            foreach (var (digest, matches) in byDigest)
            {
                if (matches.Count >= 2)
                    result.Add(new FileDuplicateGroup(lengthGroup.Key, digest, matches.AsReadOnly()));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Paths[0], right.Paths[0]));
        cancellationToken.ThrowIfCancellationRequested();
        return result.AsReadOnly();
    }

    private static async Task<string> ComputeFileHashAsync(FileSnapshot expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = new FileStream(expected.Path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });

        VerifyUnchanged(expected, stream.Length);
        var digest = await ComputeSha256Async(stream, cancellationToken).ConfigureAwait(false);
        VerifyUnchanged(expected, stream.Length);
        if (stream.Position != expected.Length)
            throw new IOException($"File length changed while hashing: {expected.Path}");
        return digest;
    }

    private static FileSnapshot ReadSnapshot(string path)
    {
        var file = new FileInfo(path);
        return new FileSnapshot(path, file.Length, file.LastWriteTimeUtc);
    }

    private static void VerifyUnchanged(FileSnapshot expected, long openFileLength)
    {
        var actual = ReadSnapshot(expected.Path);
        if (openFileLength != expected.Length || actual.Length != expected.Length || actual.LastWriteTimeUtc != expected.LastWriteTimeUtc)
            throw new IOException($"File changed while hashing: {expected.Path}");
    }

    private readonly record struct FileSnapshot(string Path, long Length, DateTime LastWriteTimeUtc);
}
