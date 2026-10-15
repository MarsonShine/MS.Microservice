using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.Microservice.Core.Auditing;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;
using MS.Microservice.Persistence.EFCore.Auditing;

namespace MS.Microservice.Lab.Controller;

public sealed class CreateAuditOrderRequest
{
    /// <summary>审批时从两条库存记录扣除的数量，默认 2，范围 1–50。</summary>
    [Range(1, 50), DefaultValue(2)] public int Quantity { get; set; } = 2;
}
public sealed record AuditDemoStockView(Guid Id, string Sku, int Available);
public sealed record AuditDemoOrderView(Guid Id, string Status, int Quantity, int Version, IReadOnlyList<AuditDemoStockView> Stock);
public sealed record AuditDemoResult(Guid OperationId, AuditDemoOrderView Order);
public sealed record AuditDemoRecord(Guid Id, int SaveSequence, string Action, string Actor, string ResourceType,
    string ResourceId, AuditChangeKind Kind, DateTimeOffset OccurredAtUtc, System.Text.Json.JsonElement Changes);

[LabOnly, Authorize]
[Route("api/lab/tools/audits")]
[Tags("工具示例：自动业务审计")]
public sealed class AuditToolsController(ToolDemoFiles files) : ToolDemoControllerBase(files)
{
    private AuditDemoContext Database => HttpContext.RequestServices.GetRequiredService<AuditDemoContext>();
    private AuditOperationContext Operation => HttpContext.RequestServices.GetRequiredService<AuditOperationContext>();
    private string Scope => TaskCenterDemo.Scope(Owner);

    /// <summary>创建一个本人订单和两条演示库存，自动记录字段变化。</summary>
    /// <remarks>提交 quantity，例如 2。仅在 Lab 私有 SQLite 中创建演示数据，不操作性能测试库。入口标签自动提供操作上下文，模型声明决定采集字段；业务代码不插入审计记录。返回 operationId 用于查询同一次保存中的全部审计明细。</remarks>
    [HttpPost("orders"), AuditOperation("lab.order.create"), Produces("application/json")]
    [ProducesResponseType<AuditDemoResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateAuditOrderRequest request)
    {
        var database = Database;
        var order = new AuditDemoOrder { Id = Guid.NewGuid(), Scope = Scope, Quantity = request.Quantity };
        database.Orders.Add(order);
        database.Stock.AddRange(new AuditDemoStock { Id = Guid.NewGuid(), OrderId = order.Id, Sku = "DEMO-A" },
            new AuditDemoStock { Id = Guid.NewGuid(), OrderId = order.Id, Sku = "DEMO-B" });
        await database.SaveChangesAsync(Token);
        return Created($"/api/lab/tools/audits/orders/{order.Id}", new AuditDemoResult(Operation.Current!.Id, await Describe(order)));
    }

    /// <summary>审批订单，同时修改订单与两条库存并新增审批记录。</summary>
    /// <remarks>使用创建接口返回的 id。一次事务涉及三张业务表、四个实体，自动生成四条审计明细，统一关联同一个 operationId 和保存序号。事务失败时业务和审计一起回滚；已审批或并发冲突返回 409。</remarks>
    [HttpPost("orders/{id:guid}/approve"), AuditOperation("lab.order.approve"), Produces("application/json")]
    [ProducesResponseType<AuditDemoResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid id)
    {
        var database = Database;
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var order = await database.Orders.SingleOrDefaultAsync(value => value.Id == id && value.Scope == Scope, Token);
        if (order is null) return NotFound();
        if (order.Status != "Pending") return Conflict(new ProblemDetails { Status = 409, Detail = "订单已审批。" });
        var stock = await database.Stock.Where(value => value.OrderId == id).ToListAsync(Token);
        if (stock.Any(value => value.Available < order.Quantity)) throw new InvalidDataException("演示库存不足。");
        order.Status = "Approved"; order.Version++;
        foreach (var item in stock) item.Available -= order.Quantity;
        database.Approvals.Add(new() { Id = Guid.NewGuid(), OrderId = id, Note = "审批通过" });
        try
        {
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        catch (DbUpdateConcurrencyException) { return Conflict(new ProblemDetails { Status = 409, Detail = "订单已变化，请重新查询。" }); }
        return Ok(new AuditDemoResult(Operation.Current!.Id, await Describe(order)));
    }

    /// <summary>查看本人演示订单和库存的当前状态。</summary>
    /// <remarks>使用创建返回的 id；其他用户和不存在的订单均返回 404。读取接口不生成业务审计记录。</remarks>
    [HttpGet("orders/{id:guid}"), Produces("application/json")]
    [ProducesResponseType<AuditDemoOrderView>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id)
    {
        var order = await Database.Orders.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id && value.Scope == Scope, Token);
        return order is null ? NotFound() : Ok(await Describe(order));
    }

    /// <summary>按操作标识查看多表变更的审计分组。</summary>
    /// <remarks>传创建或审批返回的 operationId，查看操作人、保存序号和每个实体的选定字段差异。beforeKnown=false 表示原值不可用，不能当作原值为 null。仅返回当前用户 Scope 的明细；不存在或不属于本人返回 404。</remarks>
    [HttpGet("operations/{operationId:guid}"), Produces("application/json")]
    [ProducesResponseType<IReadOnlyList<AuditDemoRecord>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Records(Guid operationId)
    {
        var records = await Database.ForAuditScope(Scope).Where(value => value.OperationId == operationId)
            .OrderBy(value => value.SaveSequence).ThenBy(value => value.ResourceType).ThenBy(value => value.ResourceId).Take(1001).ToListAsync(Token);
        if (records.Count == 0) return NotFound();
        if (records.Count > 1000) throw new InvalidDataException("演示操作查询最多 1000 条明细。");
        return Ok(records.Select(value => new AuditDemoRecord(value.Id, value.SaveSequence, value.Action, value.Actor,
            value.ResourceType, value.ResourceId, value.Kind, new(value.OccurredAtUtcTicks, TimeSpan.Zero),
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(value.ChangesJson))).ToArray());
    }
    private async Task<AuditDemoOrderView> Describe(AuditDemoOrder order) => new(order.Id, order.Status, order.Quantity, order.Version,
        await Database.Stock.AsNoTracking().Where(value => value.OrderId == order.Id).OrderBy(value => value.Sku)
            .Select(value => new AuditDemoStockView(value.Id, value.Sku, value.Available)).ToListAsync(Token));
}
