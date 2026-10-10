using Microsoft.Extensions.Options;
using MS.Microservice.Core.IO;

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
        var storageRoot = ResolveStorageRoot();
        var storedFileName = await new LocalFileStorage(storageRoot)
            .SaveAsync(content, extension, cancellationToken);
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

}
