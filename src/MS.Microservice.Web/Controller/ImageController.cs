using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MS.Microservice.Web.Application.Uploads;
using MS.Microservice.Web.Infrastructure.Uploads;
using MS.Microservice.Web.Infrastructure.Labs;
using System.Net;

namespace MS.Microservice.Web.Controller;

[LabOnly]
[Authorize]
[ApiController]
[Route("api/[controller]")]
[RequestFormLimits(MultipartBodyLengthLimit = SampleUploadOptions.MaximumRequestBodyBytes)]
[RequestSizeLimit(SampleUploadOptions.MaximumRequestBodyBytes)]
public sealed class ImageController(
    IOptions<SampleUploadOptions> options,
    FileUploadValidator uploadValidator,
    IUploadStorage uploadStorage,
    IExcelImportService excelImportService) : ControllerBase
{
    private readonly SampleUploadOptions _options = options?.Value
        ?? throw new ArgumentNullException(nameof(options));
    private readonly FileUploadValidator _uploadValidator = uploadValidator
        ?? throw new ArgumentNullException(nameof(uploadValidator));
    private readonly IUploadStorage _uploadStorage = uploadStorage
        ?? throw new ArgumentNullException(nameof(uploadStorage));
    private readonly IExcelImportService _excelImportService = excelImportService
        ?? throw new ArgumentNullException(nameof(excelImportService));

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
        try
        {
            var sql = await _excelImportService.BuildBookClassificationSqlAsync(
                Path.GetFileName(file.FileName),
                input,
                HttpContext.RequestAborted);
            return Ok(sql);
        }
        catch (InvalidOperationException)
        {
            return BadRequest("Excel 模板格式错误。");
        }
    }

}
