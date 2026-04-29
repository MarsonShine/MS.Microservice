using MS.Microservice.Web.Application.Models.Orders;

namespace MS.Microservice.Web.Application.Orders
{
    public interface IOrderQueryAppService
    {
        Task<OrderDetailsResponse?> GetAsync(Guid orderId, CancellationToken cancellationToken = default);
    }
}
