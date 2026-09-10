using Microsoft.AspNetCore.Http;

namespace MS.Microservice.Lab.Infrastructure.Uploads;

public enum UploadFileKind
{
    Image,
    Excel
}

public sealed record FileUploadValidationResult(
    bool IsValid,
    int StatusCode,
    string Message,
    string Extension)
{
    public static FileUploadValidationResult Success(string extension)
        => new(true, StatusCodes.Status200OK, string.Empty, extension);

    public static FileUploadValidationResult Failure(int statusCode, string message)
        => new(false, statusCode, message, string.Empty);
}

public sealed class FileUploadValidator
{
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] XlsxSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] XlsSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static readonly IReadOnlyDictionary<string, string> ImageContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp"
        };

    private static readonly IReadOnlyDictionary<string, string> ExcelContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

    public async Task<FileUploadValidationResult> ValidateAsync(
        IFormFile? file,
        UploadFileKind kind,
        long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        var displayName = kind == UploadFileKind.Image ? "图片" : "Excel";
        if (file is null || file.Length == 0)
        {
            return FileUploadValidationResult.Failure(
                StatusCodes.Status400BadRequest,
                $"请选择非空 {displayName} 文件。");
        }

        if (file.Length > maximumBytes)
        {
            return FileUploadValidationResult.Failure(
                StatusCodes.Status413PayloadTooLarge,
                $"{displayName} 文件超过允许大小。");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var contentTypes = kind == UploadFileKind.Image ? ImageContentTypes : ExcelContentTypes;
        if (!contentTypes.TryGetValue(extension, out var expectedContentType)
            || !string.Equals(file.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return FileUploadValidationResult.Failure(
                StatusCodes.Status415UnsupportedMediaType,
                $"不支持的 {displayName} 类型。");
        }

        await using var input = file.OpenReadStream();
        var headerLength = kind == UploadFileKind.Image ? 12 : 8;
        var header = await ReadHeaderAsync(input, headerLength, cancellationToken);
        var signatureIsValid = kind == UploadFileKind.Image
            ? HasValidImageSignature(extension, header)
            : HasValidExcelSignature(extension, header);
        if (!signatureIsValid)
        {
            return FileUploadValidationResult.Failure(
                StatusCodes.Status415UnsupportedMediaType,
                $"{displayName} 文件头与声明类型不一致。");
        }

        return FileUploadValidationResult.Success(extension);
    }

    private static async Task<byte[]> ReadHeaderAsync(
        Stream stream,
        int maximumLength,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[maximumLength];
        var totalRead = 0;
        while (totalRead < maximumLength)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(totalRead, maximumLength - totalRead),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return buffer[..totalRead];
    }

    private static bool HasValidImageSignature(string extension, ReadOnlySpan<byte> header)
        => extension switch
        {
            ".jpg" or ".jpeg" => header.StartsWith(JpegSignature),
            ".png" => header.StartsWith(PngSignature),
            ".webp" => header.Length >= 12
                && header[..4].SequenceEqual("RIFF"u8)
                && header[8..12].SequenceEqual("WEBP"u8),
            _ => false
        };

    private static bool HasValidExcelSignature(string extension, ReadOnlySpan<byte> header)
        => extension switch
        {
            ".xlsx" => header.StartsWith(XlsxSignature),
            ".xls" => header.StartsWith(XlsSignature),
            _ => false
        };
}
