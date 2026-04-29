using Microsoft.EntityFrameworkCore;
using MS.Microservice.Domain.Aggregates.OrderAggregate;
using MS.Microservice.Domain.EventSourcing;
using MS.Microservice.Infrastructure.EventSourcing;
using System.Linq;

namespace MS.Microservice.Web.Application.Orders
{
    /// <summary>
    /// 查询侧示例：同时展示事件流重放得到的当前状态和投影表中的查询结果。
    /// </summary>
    public sealed class OrderQueryAppService : IOrderQueryAppService
    {
        private readonly IEventStore _eventStore;
        private readonly EventStoreDbContext _eventStoreDbContext;

        public OrderQueryAppService(IEventStore eventStore, EventStoreDbContext eventStoreDbContext)
        {
            _eventStore = eventStore;
            _eventStoreDbContext = eventStoreDbContext;
        }

        public async Task<Models.Orders.OrderDetailsResponse?> GetAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            var streamId = OrderAggregate.GetStreamId(orderId);
            var events = await _eventStore.LoadStreamAsync<OrderEvent>(streamId, cancellationToken: cancellationToken);
            if (events.Count == 0)
            {
                return null;
            }

            var state = OrderAggregate.Fold(events.Select(x => x.Data));
            var readModel = await _eventStoreDbContext.OrderReadModels
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.OrderId == orderId.ToString("D"), cancellationToken);

            return new Models.Orders.OrderDetailsResponse(
                orderId,
                streamId,
                state.Version,
                state.CustomerId,
                state.Currency,
                readModel?.Status ?? ToStatus(state),
                readModel?.TotalAmount ?? state.TotalAmount,
                state.Lines.Values
                    .Select(line => new Models.Orders.OrderLineResponse(line.ProductId, line.UnitPrice, line.Quantity, line.Amount))
                    .OrderBy(line => line.ProductId)
                    .ToArray(),
                events.Select(@event => new Models.Orders.OrderEventResponse(
                    @event.Data.GetType().Name,
                    @event.Version,
                    @event.GlobalPosition,
                    @event.CreatedAt)).ToArray(),
                readModel?.UpdatedAt);
        }

        private static string ToStatus(OrderState state)
            => state.IsCancelled ? "Cancelled"
                : state.IsConfirmed ? "Confirmed"
                : state.Exists ? "Draft"
                : "NotCreated";
    }
}
