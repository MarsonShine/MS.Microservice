using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.AspNetCore.Uploads;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;
using MS.Microservice.Media.Codes;

namespace MS.Microservice.Lab.Controller;

[LabOnly, Authorize, ApiController]
[Route("api/lab/tools/codes")]
[RequestSizeLimit(ToolDemoFiles.MaximumRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = ToolDemoFiles.MaximumRequestBytes)]
public sealed class CodeToolsController(ToolDemoFiles files) : ToolDemoControllerBase(files)
{
    /// <summary>生成二维码或条码PNG。</summary>
    /// <remarks>JSON枚举使用数值，默认Format=0（Qr）。QR支持中文；其他格式的字符集、长度及校验位遵循码标准。采用深色前景、浅色背景，保留完整码与空白边距。</remarks>
    [HttpPost("generate"), Consumes("application/json")]
    [Produces("image/png")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Generate([FromBody] CodeGenerateRequest request)
    {
        using var output = new MemoryStream();
        await new CodeProcessor().GenerateAsync(request.Text, output, new()
        {
            Format = request.Format, Width = request.Width, Height = request.Height,
            Margin = request.Margin, QrErrorCorrection = request.QrErrorCorrection,
            Foreground = MediaToolParameters.Color(request.Foreground),
            Background = MediaToolParameters.Color(request.Background), Limits = MediaToolParameters.Limits
        }, Token);
        return Download(output.ToArray(), "image/png", "code.png");
    }

    /// <summary>识别上传图片中的二维码或条码。</summary>
    /// <remarks>返回文字、实际识别格式及EXIF方向修正后图片中的定位点；未识别到码时返回空数组。自动旋转采用首个成功方向，不保证找全同图内不同方向的所有码。</remarks>
    [HttpPost("decode"), Consumes("multipart/form-data")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<DecodedCode>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Decode([FromForm] CodeDecodeForm form)
    {
        using var input = new MemoryStream(await Files.ReadAsync(form.File, Token, UploadFileKind.Image));
        return Ok(await new CodeProcessor().DecodeAsync(input, new()
        {
            Formats = form.Formats, AutoRotate = form.AutoRotate, TryHarder = form.TryHarder,
            TryInverted = form.TryInverted, Limits = MediaToolParameters.Limits
        }, Token));
    }
}
