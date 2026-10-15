# EF Core query components

This package contains reusable specification/include evaluation, soft-delete query helpers and opt-in automatic business auditing. It depends on Core and EF Core relational APIs, not on sample domain models, a host, a broker, or a specific database context.

Applications own their DbContext, model mappings and migrations. The old Activation identity/log database is maintained under `samples/Lab/MS.Microservice.Lab.Persistence`; the production reference has its own provider-specific contexts under `samples/Reference`.

Copy this project with Core and Domain.Primitives, or consume their packages together. Choose and configure the database provider in the application.

AuditTimestampExtensions.UpdateAuditTimestamps(context, timeProvider) 可在保存钩子中显式调用。
新增实体设置其实现的 ICreatedAt / IUpdatedAt；修改实体仅设置 IUpdatedAt；未修改或删除实体
不变。一次调用使用同一个 UTC 时刻。它不提交事务，也不改动业务领域事件。

## Automatic business auditing

Derive the application context from `AuditedDbContext`, map the audit table and select the scalar fields to record. The application still owns the provider, migrations and transaction boundary.

```csharp
using Microsoft.EntityFrameworkCore;
using MS.Microservice.Core.Auditing;
using MS.Microservice.Persistence.EFCore.Auditing;

public sealed class OrdersContext(DbContextOptions<OrdersContext> options,
    AuditOperationContext operation) : AuditedDbContext(options, operation)
{
    public DbSet<Order> Orders => Set<Order>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.AddAuditRecords();
        model.Entity<Order>().HasKey(x => x.Id);
        model.Entity<Order>().Property(x => x.Id).ValueGeneratedNever();
        model.Entity<Order>().AuditChanges("order", x => new { x.Status, x.Amount });
    }
}
public sealed class Order
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "Pending";
    public decimal Amount { get; set; }
}
```

Register `AuditOperationContext` as scoped alongside the application context. Outside MVC, begin one operation around the business workflow:

```csharp
using var operation = auditOperation.Begin(tenantId, actorId, "order.approve");
var order = await database.Orders.SingleAsync(x => x.Id == orderId, cancellationToken);
order.Status = "Approved";
await database.SaveChangesAsync(cancellationToken);

var details = await database.ForAuditScope(tenantId)
    .Where(x => x.OperationId == auditOperation.Current!.Id)
    .ToListAsync(cancellationToken);
```

The [MVC adapter](../../../src/MS.Microservice.AspNetCore/README.md#automatic-audit-operations) opens this operation automatically for `[AuditOperation]` actions. Business methods only change entities and save. Four changed entities across three tables produce four detail rows sharing one `OperationId`; repeated saves use increasing `SaveSequence` values. Existing EF transactions include both business and audit rows.

| Setting / input | Default or requirement |
| --- | --- |
| `AuditChanges(resourceType, fields)` | 1–16 direct mapped scalar properties; resource type is 1–96 ASCII letters/digits/`.`/`-`/`_` |
| `Begin(scope, actor, name)` | Trusted host identity; maximum lengths 256 / 255 / 128; optional correlation ID ≤256 |
| `AuditingEnabled` | `true`; set per context before use for explicitly unaudited work |
| `AuditCollectionOptions` | Defaults: 8 KiB JSON/detail, 1,000 details, 1 MiB JSON/save. Constructor overrides allow 64 bytes–1 MiB/detail and 1–10,000 details; batch budget must be at least the detail budget |
| Resource identity | Known before save, ≤256 characters; composite keys supported; temporary database-generated keys rejected |
| Selected values | Strings, booleans, numeric/date/time values, GUIDs and enums; store-generated fields and object graphs unsupported |
| `beforeKnown` | Queried/successfully saved entities have known originals; attached entities may report unknown originals without an extra database read |

Use a relational provider, transactional saving and the normal `SaveChanges` overloads that accept changes. A selected change without an operation, an invalid model or an exceeded budget fails the save. `SaveChanges`' return count includes audit entities. Nested operations share the root identity and must be disposed in reverse order; a context/operation cannot be shared by concurrent workflows.

Use scoped `AddDbContext`, not `AddDbContextPool` or a pooled factory: the context retains its injected operation identity. Tracking-event registration initializes EF during context construction; model/provider configuration must not depend on values assigned later in the derived constructor body.

Run normalization and timestamp updates **before** calling the audited save; later save callbacks must not mutate tracked business entities. Raw SQL, `ExecuteUpdate`/`ExecuteDelete`, other contexts and other ORMs are outside this collector. After rolling back an outer transaction, clear or dispose its context before reuse. Scope filtering is a query helper: the host must authorize the scope, actor and audit-reading endpoint. Select only values permitted in retained audit data; request bodies, results and navigation graphs are never collected.

See [capture and transaction design](../../docs/automatic-auditing.md) for implementation details and [Lab's multi-table example](../../../samples/Lab/MS.Microservice.Lab/README.md#自动业务审计示例) for the API workflow.

Auditing adds persistent writes. The current [PostgreSQL measurements](../../docs/automatic-auditing-performance.md) do **not** meet the intended throughput/P95 overhead targets; validate capacity on the application's workload before enabling it in a high-throughput service.

Properties selected for audit can use Core's `[Sensitive(SensitiveKind.Phone)]`, `Email` or `Secret`. Phone/email values are masked **before audit persistence** and carry `protection=Masked`. Secret fields retain only the field-change fact with `protection=ChangedOnly`, omitting both values. Change detection still compares business originals first, so equal masks do not hide actual changes. Business values are not modified or encrypted. Classified primary keys are rejected to prevent raw values leaking through `ResourceId`; use independent opaque keys and authorized, non-sensitive operation identifiers.
