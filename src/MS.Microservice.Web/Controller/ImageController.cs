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
    IOptions<SampleUploadOptions> options,
    FileUploadValidator uploadValidator,
    IUploadStorage uploadStorage) : ControllerBase
{
    private readonly SampleUploadOptions _options = options?.Value
        ?? throw new ArgumentNullException(nameof(options));
    private readonly FileUploadValidator _uploadValidator = uploadValidator
        ?? throw new ArgumentNullException(nameof(uploadValidator));
    private readonly IUploadStorage _uploadStorage = uploadStorage
        ?? throw new ArgumentNullException(nameof(uploadStorage));

    [HttpPost("upload")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> Upload(IFormFile? file)
    {
        var validation = await _uploadValidator.ValidateAsync(
            file,
            UploadFileKind.Image,
            _options.MaxImageBytes,
            HttpContext.RequestAborted);
        if (!validation.IsValid)
        {
            return StatusCode(validation.StatusCode, validation.Message);
        }

        await using var input = file!.OpenReadStream();
        var storedUpload = await _uploadStorage.SaveAsync(
            input,
            validation.Extension,
            HttpContext.RequestAborted);

        return Ok(new
        {
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            status = StatusCodes.Status200OK,
            fileName = storedUpload.FileName
        });
    }

    [HttpPost("excel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> ExcelReader(IFormFile? file)
    {
        var validation = await _uploadValidator.ValidateAsync(
            file,
            UploadFileKind.Excel,
            _options.MaxExcelBytes,
            HttpContext.RequestAborted);
        if (!validation.IsValid)
        {
            return StatusCode(validation.StatusCode, validation.Message);
        }

        await using var input = file!.OpenReadStream();
        using var buffered = new MemoryStream(capacity: checked((int)file.Length));
        await input.CopyToAsync(buffered, HttpContext.RequestAborted);
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

}
