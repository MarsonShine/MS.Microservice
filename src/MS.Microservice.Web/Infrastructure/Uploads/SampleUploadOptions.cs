namespace MS.Microservice.Web.Infrastructure.Uploads;

public sealed class SampleUploadOptions
{
    public const string SectionName = "SampleUploadOptions";
    public const long MaximumConfiguredFileBytes = 20 * 1024 * 1024;
    public const long MaximumRequestBodyBytes = 25 * 1024 * 1024;

    public long MaxImageBytes { get; set; } = 5 * 1024 * 1024;
    public long MaxExcelBytes { get; set; } = 10 * 1024 * 1024;
    public string StorageDirectory { get; set; } = "uploads";

    public static bool IsSafeStorageDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || Path.IsPathRooted(path)
            || path.StartsWith('/')
            || path.StartsWith('\\')
            || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'))
        {
            return false;
        }

        var segments = path.Split(
            ['/', '\\'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 0 && segments.All(segment => segment is not "." and not "..");
    }
}
