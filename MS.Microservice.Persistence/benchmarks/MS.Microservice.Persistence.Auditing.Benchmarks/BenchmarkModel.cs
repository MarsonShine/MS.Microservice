using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MS.Microservice.Core.Auditing;
using MS.Microservice.Persistence.EFCore.Auditing;

namespace MS.Microservice.Persistence.Auditing.Benchmarks;

internal interface ISchemaContext { string Schema { get; } }
internal sealed class PlainContext(DbContextOptions<PlainContext> options, string schema) : DbContext(options), ISchemaContext
{
    public string Schema => schema;
    protected override void OnModelCreating(ModelBuilder model) => BenchmarkModel.Configure(model, Schema);
}
internal sealed class CapturingContext(DbContextOptions<CapturingContext> options, string schema, AuditOperationContext operation)
    : AuditedDbContext(options, operation), ISchemaContext
{
    public string Schema => schema;
    protected override void OnModelCreating(ModelBuilder model) => BenchmarkModel.Configure(model, Schema);
}
internal sealed class SchemaModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), ((ISchemaContext)context).Schema, designTime);
}
internal static class BenchmarkModel
{
    internal static void Configure(ModelBuilder model, string schema)
    {
        model.HasDefaultSchema(schema);
        model.AddAuditRecords(schema);
        model.Entity<Order>().HasKey(value => value.Id);
        model.Entity<Order>().Property(value => value.Id).ValueGeneratedNever();
        model.Entity<Order>().HasIndex(value => new { value.Worker, value.Number }).IsUnique();
        model.Entity<Order>().AuditChanges("order", value => new { value.Quantity, value.Amount, value.Approved });
        model.Entity<Stock>().HasKey(value => value.Id);
        model.Entity<Stock>().Property(value => value.Id).ValueGeneratedNever();
        model.Entity<Stock>().HasOne<Order>().WithMany().HasForeignKey(value => value.OrderId);
        model.Entity<Stock>().HasIndex(value => value.OrderId);
        model.Entity<Stock>().AuditChanges("stock", value => value.Available);
        model.Entity<Approval>().HasKey(value => value.Id);
        model.Entity<Approval>().Property(value => value.Id).ValueGeneratedNever();
        model.Entity<Approval>().HasOne<Order>().WithMany().HasForeignKey(value => value.OrderId);
        model.Entity<Approval>().AuditChanges("approval", value => value.Note);
    }
}
internal sealed class Order
{
    public Guid Id { get; set; }
    public int Worker { get; set; }
    public int Number { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal Amount { get; set; } = 12.5m;
    public bool Approved { get; set; }
    public int LastSeen { get; set; }
}
internal sealed class Stock
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public int Available { get; set; } = 100000;
}
internal sealed class Approval
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Note { get; set; } = "Approved by benchmark";
}
