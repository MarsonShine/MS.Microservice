using MS.Microservice.Core.Functional;
using MS.Microservice.Messaging;
using MS.Microservice.Reference.Domain;
using static MS.Microservice.Core.Functional.F;

namespace MS.Microservice.Reference.Application;

public sealed class OrderService(IOrderRepository orders, IUnitOfWork unit, TimeProvider clock)
{
    public async Task<Either<Error, OrderView>> CreateAsync(CreateOrder request, AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var order = Order.Create(request.Sku, request.Quantity, actor.Issuer, actor.Subject, clock.GetUtcNow());
            return await unit.ExecuteAsync<Either<Error, OrderView>>(_ =>
            {
                orders.Add(order);
                return Task.FromResult<Either<Error, OrderView>>(Right(OrderView.From(order)));
            }, cancellationToken);
        }
        catch (OrderValidationException exception) { return Left(Error.Validation(exception.Message)); }
    }
}
