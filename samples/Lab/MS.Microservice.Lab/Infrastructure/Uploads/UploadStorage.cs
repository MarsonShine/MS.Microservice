using Microsoft.Extensions.Options;

namespace MS.Microservice.Lab.Infrastructure.Uploads;

public sealed record StoredUpload(string FileName);

public interface IUploadStorage
{
    Task<StoredUpload> SaveAsync(
        Stream content,
        string extension,
        CancellationToken cancellationToken = default);
}

public sealed class LocalUploadStorage(
    IWebHostEnvironment environment,
    IOptions<SampleUploadOptions> options) : IUploadStorage
{
    private readonly IWebHostEnvironment _environment = environment
        ?? throw new ArgumentNullException(nameof(environment));
    private readonly SampleUploadOptions _options = options?.Value
        ?? throw new ArgumentNullException(nameof(options));

    public async Task<StoredUpload> SaveAsync(
        Stream content,
        string extension,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead)
        {
            throw new ArgumentException("上传内容流必须可读。", nameof(content));
        }

        var normalizedExtension = NormalizeExtension(extension);
        var storageRoot = ResolveStorageRoot();
        Directory.CreateDirectory(storageRoot);
        var storedFileName = $"{Guid.NewGuid():N}{normalizedExtension}";
        var storedFilePath = Path.Combine(storageRoot, storedFileName);

        try
        {
            await using var output = new FileStream(
                storedFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            await content.CopyToAsync(output, cancellationToken);
        }
        catch
        {
            System.IO.File.Delete(storedFilePath);
            throw;
        }

        return new StoredUpload(storedFileName);
    }

    private string ResolveStorageRoot()
    {
        var contentRoot = Path.GetFullPath(_environment.ContentRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var storageRoot = Path.GetFullPath(Path.Combine(contentRoot, _options.StorageDirectory));
        if (!storageRoot.StartsWith(contentRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("上传目录必须位于应用内容根目录下。");
        }

        return storageRoot;
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)
            || extension.Length > 10
            || extension[0] != '.'
            || extension.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException("上传文件扩展名无效。", nameof(extension));
        }

        return extension.ToLowerInvariant();
    }
}
