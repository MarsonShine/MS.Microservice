using System.IO.Compression;

namespace MS.Microservice.Core.IO;

/// <summary>Streams ordinary files and directories to and from ZIP archives.</summary>
public static class ZipArchiveHelper
{
    private const int BufferSize = 81_920;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly uint[] Crc32Table = CreateCrc32Table();

    /// <summary>
    /// Archives a directory's contents, including empty directories. The destination remains open;
    /// cancellation or failure can leave a partial ZIP in the caller-owned stream.
    /// </summary>
    public static async Task CreateZipAsync(string sourceDirectory, Stream destination,
        CompressionLevel compressionLevel = CompressionLevel.Optimal,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        if (!Enum.IsDefined(compressionLevel))
            throw new ArgumentOutOfRangeException(nameof(compressionLevel));

        var source = Path.GetFullPath(sourceDirectory);
        EnsureNoLinks(source);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException(source);
        if (destination is FileStream file && IsWithin(Path.GetFullPath(file.Name), source))
            throw new ArgumentException("The ZIP output must be outside the source directory.", nameof(destination));
        cancellationToken.ThrowIfCancellationRequested();

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var pending = new Stack<(DirectoryInfo Directory, string Prefix)>();
        pending.Push((new DirectoryInfo(source), ""));
        while (pending.TryPop(out var current))
        {
            EnsureNoLinks(current.Directory.FullName);
            foreach (var item in current.Directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureNoLinks(item.FullName);
                var directory = (item.Attributes & FileAttributes.Directory) != 0;
                var name = current.Prefix + item.Name + (directory ? "/" : "");
                NormalizeEntryName(name, fromFileSystem: true);
                var entry = archive.CreateEntry(name, compressionLevel);
                if (directory)
                {
                    pending.Push((new DirectoryInfo(item.FullName), name));
                    continue;
                }

                await using var input = new FileStream(item.FullName, FileMode.Open, FileAccess.Read,
                    FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var output = entry.Open();
                await input.CopyToAsync(output, BufferSize, cancellationToken).ConfigureAwait(false);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Extracts a readable, seekable ZIP into a new directory, publishing only after all entries succeed.
    /// Keeps the source open and rejects existing destinations and filesystem links.
    /// </summary>
    public static async Task ExtractZipAsync(Stream source, string destinationDirectory,
        ZipExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("The source stream must be readable and seekable.", nameof(source));
        options ??= new ZipExtractionOptions();
        if (options.MaxEntries < 0 || options.MaxEntryBytes < 0 || options.MaxTotalBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "ZIP limits cannot be negative.");

        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDirectory));
        var parent = Path.GetDirectoryName(destination);
        if (parent is null)
            throw new ArgumentException("A filesystem root cannot be the extraction destination.", nameof(destinationDirectory));
        EnsureNoLinks(destination);
        if (Exists(destination))
            throw new IOException("The extraction destination already exists.");
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException("The extraction destination's parent must exist.");
        cancellationToken.ThrowIfCancellationRequested();

        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > options.MaxEntries)
            throw new InvalidDataException("The ZIP entry count exceeds the configured limit.");

        var staging = Path.Combine(parent, ".zip-" + Guid.NewGuid().ToString("N"));
        if (Exists(staging))
            throw new IOException("The staging directory already exists.");
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(staging);
        else
            Directory.CreateDirectory(staging, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            var paths = new Dictionary<string, bool>(PathComparer);
            var explicitPaths = new HashSet<string>(PathComparer);
            var buffer = new byte[BufferSize];
            long totalBytes = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (name, directory) = NormalizeEntryName(entry.FullName);
                ValidateEntryType(entry, directory);
                RegisterPath(name, directory, paths, explicitPaths);
                var target = Path.GetFullPath(Path.Combine(staging, name.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsWithin(target, staging))
                    throw new InvalidDataException("A ZIP entry escapes its extraction directory.");
                EnsureNoLinks(target);
                if (directory)
                {
                    await using var content = entry.Open();
                    if (await content.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) != 0)
                        throw new InvalidDataException("Directory entries cannot contain file content.");
                    ValidateContent(entry, 0, uint.MaxValue);
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = entry.Open();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                long entryBytes = 0;
                var checksum = uint.MaxValue;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    if (read > options.MaxEntryBytes - entryBytes || read > options.MaxTotalBytes - totalBytes)
                        throw new InvalidDataException("The decompressed ZIP content exceeds the configured byte limits.");
                    entryBytes += read;
                    totalBytes += read;
                    checksum = UpdateCrc32(checksum, buffer.AsSpan(0, read));
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                ValidateContent(entry, entryBytes, checksum);
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoLinks(destination);
            Directory.Move(staging, destination);
        }
        catch (Exception extractionError)
        {
            // A failed publication must not delete a destination created by another caller.
            try
            {
                Directory.Delete(staging, recursive: true);
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException("ZIP extraction failed and its temporary files could not be removed.",
                    extractionError, cleanupError);
            }
            throw;
        }
    }

    private static void ValidateContent(ZipArchiveEntry entry, long bytes, uint checksum)
    {
        if (bytes != entry.Length || ~checksum != entry.Crc32)
            throw new InvalidDataException("The ZIP entry's decompressed length or CRC32 checksum is inconsistent.");
    }

    // ZIP CRC32 uses the reflected IEEE polynomial, initial all-one bits and a final complement.
    internal static uint UpdateCrc32(uint checksum, ReadOnlySpan<byte> content)
    {
        foreach (var value in content)
            checksum = Crc32Table[(checksum ^ value) & 0xff] ^ (checksum >> 8);
        return checksum;
    }

    private static uint[] CreateCrc32Table()
    {
        var table = new uint[256];
        for (var index = 0; index < table.Length; index++)
        {
            var value = (uint)index;
            for (var bit = 0; bit < 8; bit++)
                value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xEDB88320U);
            table[index] = value;
        }
        return table;
    }

    private static (string Name, bool Directory) NormalizeEntryName(string name, bool fromFileSystem = false)
    {
        if (fromFileSystem && name.Contains('\\'))
            throw new InvalidDataException("Source names cannot contain literal ZIP separator characters.");
        var normalized = name.Replace('\\', '/');
        var directory = normalized.EndsWith('/');
        if (directory)
            normalized = normalized[..^1];
        var segments = normalized.Split('/');
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.StartsWith(' ') ||
                segment.EndsWith(' ') || segment.EndsWith('.') ||
                segment.Any(c => c < 32 || c is ':' or '*' or '?' or '"' or '<' or '>' or '|') || IsDeviceName(segment))
                throw new InvalidDataException("ZIP entry names must be portable relative paths without traversal.");
        }
        return (normalized, directory);
    }

    private static bool IsDeviceName(string segment)
    {
        var stem = segment.Split('.')[0].TrimEnd(' ');
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9' or '¹' or '²' or '³');
    }

    private static void ValidateEntryType(ZipArchiveEntry entry, bool directory)
    {
        var attributes = (uint)entry.ExternalAttributes;
        var unixType = (attributes >> 16) & 0xF000;
        if ((attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
            unixType is not (0 or 0x8000 or 0x4000) ||
            (!directory && (unixType == 0x4000 || (attributes & (uint)FileAttributes.Directory) != 0)) ||
            (directory && unixType == 0x8000))
            throw new InvalidDataException("ZIP links, special files and conflicting entry types are unsupported.");
    }

    private static void RegisterPath(string name, bool directory,
        Dictionary<string, bool> paths, HashSet<string> explicitPaths)
    {
        for (var index = name.IndexOf('/'); index >= 0; index = name.IndexOf('/', index + 1))
        {
            var ancestor = name[..index];
            if (paths.TryGetValue(ancestor, out var ancestorIsDirectory) && !ancestorIsDirectory)
                throw new InvalidDataException("A ZIP file entry is also used as a directory.");
            paths[ancestor] = true;
        }

        if (!explicitPaths.Add(name) || (paths.TryGetValue(name, out var existingDirectory) && (!directory || !existingDirectory)))
            throw new InvalidDataException("Duplicate or conflicting ZIP paths are unsupported.");
        paths[name] = directory;
    }

    private static bool IsWithin(string path, string directory)
    {
        var prefix = Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;
        return path.Equals(directory, PathComparison) || path.StartsWith(prefix, PathComparison);
    }

    private static bool Exists(string path)
    {
        try { File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void EnsureNoLinks(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Filesystem links and reparse points are unsupported.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
