using MS.Microservice.Core.Functional;
using MS.Microservice.Web.Application.Models.Orders;

namespace MS.Microservice.Web.Application.Orders
{
    public interface IOrderWorkflowAppService
    {
        Task<Either<Error, OrderCommandResult>> CreateAsync(Guid orderId, CreateOrderRequest request, CancellationToken cancellationToken = default);

        Task<Either<Error, OrderCommandResult>> AddItemAsync(Guid orderId, AddOrderItemRequest request, CancellationToken cancellationToken = default);

        Task<Either<Error, OrderCommandResult>> RemoveItemAsync(Guid orderId, RemoveOrderItemRequest request, CancellationToken cancellationToken = default);

        Task<Either<Error, OrderCommandResult>> ConfirmAsync(Guid orderId, CancellationToken cancellationToken = default);

        Task<Either<Error, OrderCommandResult>> CancelAsync(Guid orderId, CancelOrderRequest request, CancellationToken cancellationToken = default);
    }
}
