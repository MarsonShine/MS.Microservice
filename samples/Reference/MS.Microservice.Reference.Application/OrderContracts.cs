using MS.Microservice.Reference.Domain;

namespace MS.Microservice.Reference.Application;

public sealed record CreateOrder(string Sku, int Quantity);
public sealed record OrderView(Guid Id, string Sku, int Quantity, DateTimeOffset CreatedAtUtc)
{
    public static OrderView From(Order order) => new(order.Id, order.Sku, order.Quantity, order.CreatedAtUtc);
}

public interface IOrderRepository
{
    Task<Order?> GetAsync(Guid id, AuditActor actor, CancellationToken cancellationToken);
    void Add(Order order);
}
