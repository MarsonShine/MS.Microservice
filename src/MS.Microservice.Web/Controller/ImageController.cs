using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MS.Microservice.Infrastructure.Utils;
using MS.Microservice.Web.Application.Models;
using MS.Microservice.Web.Infrastructure.Uploads;
using System.Net;
using System.Text;

namespace MS.Microservice.Web.Controller;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[RequestFormLimits(MultipartBodyLengthLimit = SampleUploadOptions.MaximumRequestBodyBytes)]
[RequestSizeLimit(SampleUploadOptions.MaximumRequestBodyBytes)]
public sealed class ImageController(
    IWebHostEnvironment environment,
    IOptions<SampleUploadOptions> options) : ControllerBase
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

    private readonly IWebHostEnvironment _environment = environment
        ?? throw new ArgumentNullException(nameof(environment));
    private readonly SampleUploadOptions _options = options?.Value
        ?? throw new ArgumentNullException(nameof(options));

    [HttpPost("upload")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> Upload(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("请选择非空图片文件。");
        }

        if (file.Length > _options.MaxImageBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, "图片文件超过允许大小。");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!ImageContentTypes.TryGetValue(extension, out var expectedContentType)
            || !string.Equals(file.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, "不支持的图片类型。");
        }

        await using var input = file.OpenReadStream();
        var header = await ReadHeaderAsync(input, 12, HttpContext.RequestAborted);
        if (!HasValidImageSignature(extension, header))
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, "图片文件头与声明类型不一致。");
        }

        var storageRoot = ResolveStorageRoot();
        Directory.CreateDirectory(storageRoot);
        var storedFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
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
            await output.WriteAsync(header, HttpContext.RequestAborted);
            await input.CopyToAsync(output, HttpContext.RequestAborted);
        }
        catch
        {
            System.IO.File.Delete(storedFilePath);
            throw;
        }

        return Ok(new
        {
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            status = StatusCodes.Status200OK,
            fileName = storedFileName
        });
    }

    [HttpPost("excel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> ExcelReader(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("请选择非空 Excel 文件。");
        }

        if (file.Length > _options.MaxExcelBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, "Excel 文件超过允许大小。");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!ExcelContentTypes.TryGetValue(extension, out var expectedContentType)
            || !string.Equals(file.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, "不支持的 Excel 类型。");
        }

        await using var input = file.OpenReadStream();
        using var buffered = new MemoryStream(capacity: checked((int)file.Length));
        await input.CopyToAsync(buffered, HttpContext.RequestAborted);
        buffered.Position = 0;
        var header = await ReadHeaderAsync(buffered, 8, HttpContext.RequestAborted);
        if (!HasValidExcelSignature(extension, header))
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, "Excel 文件头与声明类型不一致。");
        }

        buffered.Position = 0;
        var excelHelper = new ExcelHelper()
            .InitSheetIndex(0)
            .InitStartReadRowIndex(0, 1);
        List<ExcelDemoRequest> list;
        try
        {
            list = excelHelper.Import<ExcelDemoRequest>(Path.GetFileName(file.FileName), buffered);
        }
        catch (InvalidOperationException)
        {
            return BadRequest("Excel 模板格式错误。");
        }
        finally
        {
            excelHelper.Workbook?.Dispose();
        }

        var sql = new StringBuilder();
        foreach (var item in list)
        {
            foreach (var bookClassify in item.BookClassify)
            {
                sql.AppendLine(
                    $"INSERT INTO [dbo].[book_type]([BookId], [ClassifyId]) VALUES ({item.BookId}, {(int)bookClassify});");
            }
        }

        return Ok(sql.ToString());
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
        => extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => header.StartsWith(JpegSignature),
            ".png" => header.StartsWith(PngSignature),
            ".webp" => header.Length >= 12
                && header[..4].SequenceEqual("RIFF"u8)
                && header[8..12].SequenceEqual("WEBP"u8),
            _ => false
        };

    private static bool HasValidExcelSignature(string extension, ReadOnlySpan<byte> header)
        => extension.ToLowerInvariant() switch
        {
            ".xlsx" => header.StartsWith(XlsxSignature),
            ".xls" => header.StartsWith(XlsSignature),
            _ => false
        };
}
