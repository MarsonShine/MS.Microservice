using MS.Microservice.Lab.Application.Models.Orders;

namespace MS.Microservice.Lab.Application.Orders
{
    public interface IOrderQueryAppService
    {
        Task<OrderDetailsResponse?> GetAsync(Guid orderId, CancellationToken cancellationToken = default);
    }
}
