using Microsoft.EntityFrameworkCore;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Domain;

namespace MS.Microservice.Reference.Persistence;

public sealed class OrderRepository(ReferenceDbContext context) : IOrderRepository
{
    public Task<Order?> GetAsync(Guid id, AuditActor actor, CancellationToken cancellationToken)
        => context.Orders.AsNoTracking().SingleOrDefaultAsync(order => order.Id == id
            && order.OwnerIssuer == actor.Issuer && order.OwnerSubject == actor.Subject, cancellationToken);

    public void Add(Order order) => context.Orders.Add(order);
}
