using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.Microservice.Core.Auditing;
using MS.Microservice.Core.Security.SensitiveData;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;
using MS.Microservice.Persistence.EFCore.Auditing;

namespace MS.Microservice.Lab.Controller;

public sealed class SensitiveDemoRequest
{
    [Required, StringLength(64), DefaultValue("示例用户")] public string Name { get; set; } = "示例用户";
    [Required, StringLength(32), DefaultValue("13800138000"), Sensitive(SensitiveKind.Phone)] public string Phone { get; set; } = "13800138000";
    [Required, StringLength(320), DefaultValue("alice@example.com"), Sensitive(SensitiveKind.Email)] public string Email { get; set; } = "alice@example.com";
    /// <summary>仅作 Secret 分类演示的内部备注，最多 256 字符；请使用虚构数据。</summary>
    [StringLength(256), DefaultValue("示例内部备注"), Sensitive(SensitiveKind.Secret)] public string InternalNote { get; set; } = "示例内部备注";
}
public sealed record SensitivePreviewResult(SensitiveDemoRequest Value, IReadOnlyDictionary<string, object?> ExportFields);
public sealed record SensitiveDemoView(Guid Id, string Name,
    [property: Sensitive(SensitiveKind.Phone)] string Phone, [property: Sensitive(SensitiveKind.Email)] string Email);
public sealed record SensitiveWriteResult(Guid OperationId, SensitiveDemoView Record);

[LabOnly, Authorize, Route("api/lab/tools/sensitive-data"), Tags("工具示例：敏感字段处理")]
public sealed class SensitiveDataToolsController(ToolDemoFiles files, ILogger<SensitiveDataToolsController> logger) : ToolDemoControllerBase(files)
{
    private SensitiveDemoContext Database => HttpContext.RequestServices.GetRequiredService<SensitiveDemoContext>();
    private string Scope => TaskCenterDemo.Scope(Owner);
    private Guid OperationId => HttpContext.RequestServices.GetRequiredService<AuditOperationContext>().Current!.Id;

    /// <summary>预览 JSON 脱敏、受保护日志及导出字段投影。</summary>
    /// <remarks>默认值即可运行。Phone／Email 输出掩码，InternalNote 从响应、导出投影和结构化日志中排除。日志通过宿主现有 ILogger 后端查看。本接口不写数据库；规则不改变请求对象原值。</remarks>
    [HttpPost("preview"), Produces("application/json"), ProducesResponseType<SensitivePreviewResult>(StatusCodes.Status200OK)]
    public IActionResult Preview(SensitiveDemoRequest request)
    {
        logger.LogSensitive(LogLevel.Information, "lab.sensitive.preview", request);
        return Ok(new SensitivePreviewResult(request, SensitiveValues.Project(request)));
    }

    /// <summary>下载按敏感字段规则处理的 CSV 示例。</summary>
    /// <remarks>Phone／Email 掩码，InternalNote 不进入表头或数据。输出为 UTF-8 CSV；可能被电子表格解释为公式的文本加前导单引号。此接口演示同步保护输出，后台导入导出编排另见 roadmap。</remarks>
    [HttpPost("export"), Produces("text/csv"), ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Export(SensitiveDemoRequest request)
    {
        var fields = SensitiveValues.Project(request);
        await using var output = new MemoryStream();
        IReadOnlyList<string?>[] rows = [fields.Keys.ToArray(), fields.Values.Select(value => SpreadsheetText(Convert.ToString(value, CultureInfo.InvariantCulture))).ToArray()];
        await MS.Microservice.Infrastructure.Utils.CsvHelper.WriteAsync(output, rows, cancellationToken: Token);
        return File(output.ToArray(), "text/csv; charset=utf-8", "protected.csv");
    }

    /// <summary>创建本人示例记录，观察同事务审计的字段保护。</summary>
    /// <remarks>业务原值保存在 Lab 私有 SQLite，返回 JSON 与审计中的 Phone／Email 为掩码，InternalNote 只记录变更事实。输出保护不等于数据库加密，请使用虚构数据。返回 operationId 可查询明细。</remarks>
    [HttpPost("records"), AuditOperation("lab.sensitive.create"), Produces("application/json")]
    [ProducesResponseType<SensitiveWriteResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SensitiveDemoRequest request)
    {
        var row = new SensitiveDemoEntity { Id = Guid.CreateVersion7(), Scope = Scope };
        Apply(row, request); Database.Records.Add(row); await Database.SaveChangesAsync(Token);
        return Created($"/api/lab/tools/sensitive-data/records/{row.Id}", new SensitiveWriteResult(OperationId, View(row)));
    }

    /// <summary>修改本人示例记录，并自动生成受保护的审计差异。</summary>
    /// <remarks>使用创建返回的 id。可将 Phone 从 13800138000 改为 13899118000：两者掩码相同，仍会记录真实字段变化并标明 protection=Masked。InternalNote 不保留任何前后值。</remarks>
    [HttpPut("records/{id:guid}"), AuditOperation("lab.sensitive.update"), Produces("application/json")]
    [ProducesResponseType<SensitiveWriteResult>(StatusCodes.Status200OK), ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, SensitiveDemoRequest request)
    {
        var row = await Database.Records.SingleOrDefaultAsync(value => value.Id == id && value.Scope == Scope, Token);
        if (row is null) return NotFound();
        Apply(row, request); await Database.SaveChangesAsync(Token);
        return Ok(new SensitiveWriteResult(OperationId, View(row)));
    }

    /// <summary>查看本人记录的受保护响应。</summary>
    /// <remarks>其他用户及不存在的记录返回 404；JSON 输出保护不修改数据库中的原值。</remarks>
    [HttpGet("records/{id:guid}"), Produces("application/json")]
    [ProducesResponseType<SensitiveDemoView>(StatusCodes.Status200OK), ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id)
    {
        var row = await Database.Records.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id && value.Scope == Scope, Token);
        return row is null ? NotFound() : Ok(View(row));
    }

    /// <summary>查看本人的受保护审计明细。</summary>
    /// <remarks>传创建或修改返回的 operationId。Masked 的前后值仅供展示；ChangedOnly 只表示字段变更，没有原值或新值。查询按本人 Scope 隔离。</remarks>
    [HttpGet("operations/{operationId:guid}"), Produces("application/json")]
    [ProducesResponseType<IReadOnlyList<AuditDemoRecord>>(StatusCodes.Status200OK), ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Audit(Guid operationId)
    {
        var records = await Database.ForAuditScope(Scope).Where(value => value.OperationId == operationId).Take(100).ToArrayAsync(Token);
        if (records.Length == 0) return NotFound();
        return Ok(records.Select(value => new AuditDemoRecord(value.Id, value.SaveSequence, value.Action, value.Actor,
            value.ResourceType, value.ResourceId, value.Kind, new(value.OccurredAtUtcTicks, TimeSpan.Zero),
            JsonSerializer.Deserialize<JsonElement>(value.ChangesJson))).ToArray());
    }

    private static void Apply(SensitiveDemoEntity row, SensitiveDemoRequest request)
    { row.Name = request.Name; row.Phone = request.Phone; row.Email = request.Email; row.InternalNote = request.InternalNote; }
    private static SensitiveDemoView View(SensitiveDemoEntity row) => new(row.Id, row.Name, row.Phone, row.Email);
    private static string? SpreadsheetText(string? value)
    {
        if (value is null) return null;
        var candidate = value.AsSpan().TrimStart();
        return candidate.Length > 0 && (candidate[0] is '=' or '+' or '-' or '@' || value[0] is '\t' or '\r' or '\n') ? "'" + value : value;
    }
}
