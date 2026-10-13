using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;
using MS.Microservice.Notifications.Email;
using MS.Microservice.Notifications.Inbox;
using MS.Microservice.Notifications.Sms;
using MS.Microservice.Notifications.Templates;

namespace MS.Microservice.Lab.Controller;

public sealed class TemplateRenderRequest
{
    /// <summary>包含 {{Name}} 或 {{User.Name}} 的模板，最多 4096 字符。</summary>
    [Required, StringLength(4096)]
    public string Template { get; set; } = "你好，{{Name}}。";
    /// <summary>模板变量，最多 32 项；名称最多 64、值最多 2048 字符，null 作为空文本。</summary>
    [Required]
    public Dictionary<string, string?> Values { get; set; } = new() { ["Name"] = "张三" };
    /// <summary>是否编码替换值以用于 HTML 文本节点，默认 false。</summary>
    public bool HtmlEncodeValues { get; set; }
}

public sealed class DemoEmailRequest
{
    /// <summary>主要收件人邮箱，1–10 项；不支持邮件附件。</summary>
    [Required, MinLength(1), MaxLength(10)]
    public List<string> To { get; set; } = [];
    /// <summary>抄送邮箱，最多 10 项，默认空。</summary>
    [MaxLength(10)]
    public List<string> Cc { get; set; } = [];
    /// <summary>密送邮箱，最多 10 项，默认空。</summary>
    [MaxLength(10)]
    public List<string> Bcc { get; set; } = [];
    /// <summary>邮件主题，最多 200 字符。</summary>
    [Required, StringLength(200)]
    public string Subject { get; set; } = "工具演示通知";
    /// <summary>纯文本正文，最多 16,000 字符；至少提供一种正文。</summary>
    [StringLength(16_000)]
    public string? TextBody { get; set; }
    /// <summary>HTML 正文，最多 64,000 字符；模板和 HTML 的可信性由调用者负责。</summary>
    [StringLength(64_000)]
    public string? HtmlBody { get; set; }
}

public sealed class DemoSmsRequest
{
    /// <summary>一个接收号码，6–20 个 ASCII 数字及可选前导 +。</summary>
    [Required, StringLength(21)]
    public string PhoneNumber { get; set; } = "";
    /// <summary>服务商已审核的短信签名，最多 256 字符。</summary>
    [Required, StringLength(256)]
    public string SignName { get; set; } = "";
    /// <summary>已审核的 SMS_ 模板编码，最多 128 字符。</summary>
    [Required, StringLength(128)]
    public string TemplateCode { get; set; } = "";
    /// <summary>按审核模板提供字符串变量；最多 32 项，验证码应保留前导零。</summary>
    public Dictionary<string, string>? TemplateParameters { get; set; }
    /// <summary>可选业务关联标识，最多 256 字符，不是幂等键。</summary>
    [StringLength(256)]
    public string? OutId { get; set; }
}

public sealed class InboxPublishRequest
{
    /// <summary>站内通知标题，最多 200 字符。</summary>
    [Required, StringLength(200)]
    public string Subject { get; set; } = "处理完成";
    /// <summary>纯文本正文，最多 16,000 字符；允许空文本。</summary>
    [Required(AllowEmptyStrings = true), StringLength(16_000)]
    public string Body { get; set; } = "你的演示结果已生成。";
    /// <summary>可选站内路径，以 / 开始，最多 2048 字符。</summary>
    [StringLength(2048)]
    public string? ActionPath { get; set; }
}

public sealed record TemplateRenderResult(string Text);
public sealed record InboxReadResult(string Id, DateTimeOffset ReadAtUtc);

[LabOnly]
[Route("api/lab/tools/notifications")]
[Tags("工具示例：邮件、短信与站内通知")]
public sealed class NotificationToolsController(ToolDemoFiles files, IConfiguration configuration, IWebHostEnvironment environment)
    : ToolDemoControllerBase(files)
{
    /// <summary>渲染命名模板，返回可用于通知正文的文本。</summary>
    /// <remarks>例如 template="你好，{{Name}}。"、values={"Name":"张三"}。替换只执行一次，不运行代码；缺少变量或错误语法返回错误。htmlEncodeValues 只适用于 HTML 文本节点，不净化模板，也不适用于 JavaScript、CSS 或 URL。</remarks>
    [HttpPost("templates/render"), Consumes("application/json"), Produces("application/json")]
    [RequestSizeLimit(262_144)]
    [ProducesResponseType<TemplateRenderResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public IActionResult RenderTemplate(TemplateRenderRequest request)
    {
        _ = Owner;
        if (request.Values.Count > 32 || request.Values.Any(pair => pair.Key.Length > 64 || pair.Value?.Length > 2048))
            throw new ArgumentException("模板变量数量、名称或值超过演示限制。");
        try { return Ok(new TemplateRenderResult(NamedTokenTemplate.Render(request.Template, request.Values, request.HtmlEncodeValues))); }
        catch (KeyNotFoundException) { return BadRequest(new ProblemDetails { Status = 400, Title = "模板缺少需要的变量。" }); }
    }

    /// <summary>通过配置好的 SMTP 实际发送一封邮件，返回服务器接受回执。</summary>
    /// <remarks>宿主须配置 ToolDemos:Smtp 的 Host、TLS、凭据和 FromAddress，可设置 FromDisplayName；请求只提供收件人、主题和正文。发送会产生真实邮件，不自动重试。成功只表示 SMTP 已接受，超时或断线时可能已发送，不能据异常直接重发。</remarks>
    [HttpPost("email"), Consumes("application/json"), Produces("application/json")]
    [RequestSizeLimit(1_048_576)]
    [ProducesResponseType<SmtpAcceptanceReceipt>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> SendEmail(DemoEmailRequest request)
    {
        _ = Owner;
        var (sender, from) = Smtp();
        if (request.To.Concat(request.Cc).Concat(request.Bcc).Any(address => address is null || address.Length > 320))
            throw new ArgumentException("收件人邮箱最多 320 字符。");
        var receipt = await sender.SendAsync(new EmailMessage
        {
            From = from, To = request.To.Select(address => new EmailAddress(address)).ToArray(),
            Cc = request.Cc.Select(address => new EmailAddress(address)).ToArray(),
            Bcc = request.Bcc.Select(address => new EmailAddress(address)).ToArray(),
            Subject = request.Subject, TextBody = request.TextBody, HtmlBody = request.HtmlBody
        }, Token);
        return Ok(receipt);
    }

    /// <summary>通过阿里云实际提交一条模板短信，返回服务商接受回执。</summary>
    /// <remarks>宿主须配置 ToolDemos:Sms 的 AccessKeyId、AccessKeySecret，可选 STS SecurityToken；请求使用已审核签名和模板。发送可能收费，服务商拒绝返回 422。SDK 没有在途取消接口，只使用配置的 HTTP 超时；成功不表示手机送达，不自动重试，OutId 不提供去重。</remarks>
    [HttpPost("sms"), Consumes("application/json"), Produces("application/json")]
    [RequestSizeLimit(131_072)]
    [ProducesResponseType<SmsAcceptanceReceipt>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> SendSms(DemoSmsRequest request)
    {
        _ = Owner;
        Token.ThrowIfCancellationRequested();
        var sender = Sms();
        try
        {
            return Ok(await sender.SendAsync(new(request.PhoneNumber, request.SignName, request.TemplateCode)
                { TemplateParameters = request.TemplateParameters, OutId = request.OutId }));
        }
        catch (SmsRejectedException error)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Status = 422, Title = "短信服务商拒绝请求。",
                Extensions = { ["code"] = error.Code, ["requestId"] = error.RequestId }
            });
        }
    }

    /// <summary>为当前登录用户发布一条持久化站内通知。</summary>
    /// <remarks>提交 subject、body 和可选 actionPath；owner 从登录身份取得，不能代其他用户发布。内容按纯文本保存在私有目录，重启后保留；宿主负责历史保留期。</remarks>
    [HttpPost("inbox"), Consumes("application/json"), Produces("application/json")]
    [RequestSizeLimit(131_072)]
    [ProducesResponseType<InboxNotification>(StatusCodes.Status201Created)]
    public async Task<IActionResult> PublishInbox(InboxPublishRequest request)
    {
        var result = await Inbox().PublishAsync(Owner, request.Subject, request.Body, request.ActionPath, Token);
        return Created("/api/lab/tools/notifications/inbox", result);
    }

    /// <summary>分页读取本人的通知，并返回总数和未读数。</summary>
    /// <param name="skip">跳过数量，默认 0，范围 0–10000。</param>
    /// <param name="take">页大小，默认 20，范围 1–100。</param>
    /// <remarks>按创建时间和 Id 倒序。offset 分页不是数据库快照；并发新增可能重复或跳项。一次最多扫描 10,000 条／32 MiB，超限报错，不返回不完整计数。</remarks>
    [HttpGet("inbox"), Produces("application/json")]
    [ProducesResponseType<InboxPage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInbox([FromQuery, Range(0, 10_000)] int skip = 0, [FromQuery, Range(1, 100)] int take = 20)
        => Ok(await Inbox().ListAsync(Owner, skip, take, Token));

    /// <summary>将本人的一条站内通知标记已读，返回首次成功记录的时间。</summary>
    /// <remarks>id 使用发布或列表返回的通知 Id；重复调用保留首次已读时间。其他用户的通知和不存在的 Id 均返回 404。</remarks>
    [HttpPost("inbox/{id}/read"), Produces("application/json")]
    [ProducesResponseType<InboxReadResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(string id)
    {
        try { return Ok(new InboxReadResult(id, await Inbox().MarkReadAsync(Owner, id, Token))); }
        catch (FileNotFoundException) { return NotFound(); }
    }

    private LocalNotificationInbox Inbox() => new(Path.Combine(new DemoFileStore(environment, configuration).Root, "inbox"));

    private (SmtpEmailSender Sender, EmailAddress From) Smtp()
    {
        const string key = "ToolDemos:Smtp";
        try
        {
            var section = configuration.GetSection(key);
            var options = section.Get<SmtpEmailOptions>();
            var from = section["FromAddress"];
            if (options is null || section.GetValue<bool?>("Enabled") != true || string.IsNullOrWhiteSpace(from)
                || from.Length > 320 || !new EmailAddressAttribute().IsValid(from)) throw new ArgumentException();
            return (new(options), new(from, section["FromDisplayName"]));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { throw new ToolDependencyException(key); }
    }

    private AliyunSmsSender Sms()
    {
        const string key = "ToolDemos:Sms";
        try
        {
            var section = configuration.GetSection(key);
            var options = section.Get<AliyunSmsOptions>();
            if (options is null || section.GetValue<bool?>("Enabled") != true) throw new ArgumentException();
            return new(options);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { throw new ToolDependencyException(key); }
    }
}
