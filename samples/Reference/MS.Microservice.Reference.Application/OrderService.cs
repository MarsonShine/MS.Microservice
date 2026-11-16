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
            var result = await unit.ExecuteAsync<Outcome>(_ =>
            {
                orders.Add(order);
                return Task.FromResult<Outcome>(OrderView.From(order));
            }, cancellationToken);
            return ToEither(result);
        }
        catch (OrderValidationException exception) { return ToEither(Error.Validation(exception.Message)); }
    }

    private union Outcome(Error, OrderView);

    private static Either<Error, OrderView> ToEither(Outcome result) => result switch
    {
        Error error => Left(error),
        OrderView order => Right(order),
        null => throw new InvalidOperationException("An order operation returned no result.")
    };
}
