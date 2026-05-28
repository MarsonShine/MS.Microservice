using MS.Microservice.Core.Functional;
using MS.Microservice.Domain.Aggregates.OrderAggregate;
using MS.Microservice.Domain.EventSourcing;
using MS.Microservice.Infrastructure.EventSourcing.Orders;
using MS.Microservice.Web.Application.Models.Orders;
using System.Linq;
using static MS.Microservice.Core.Functional.F;

namespace MS.Microservice.Web.Application.Orders
{
    /// <summary>
    /// 将 Web 层请求编排为订单事件溯源工作流：
    /// 控制器只负责 HTTP，应用服务负责命令映射、调用核心服务、触发投影并返回可序列化结果。
    /// </summary>
    public sealed class OrderWorkflowAppService : IOrderWorkflowAppService
    {
        private readonly OrderCommandService _orderCommandService;
        private readonly OrderReadModelProjector _orderReadModelProjector;
        private readonly IEventStore _eventStore;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public OrderWorkflowAppService(
            OrderCommandService orderCommandService,
            OrderReadModelProjector orderReadModelProjector,
            IEventStore eventStore,
            IHttpContextAccessor httpContextAccessor)
        {
            _orderCommandService = orderCommandService;
            _orderReadModelProjector = orderReadModelProjector;
            _eventStore = eventStore;
            _httpContextAccessor = httpContextAccessor;
        }

        public Task<Either<Error, OrderCommandResult>> CreateAsync(Guid orderId, CreateOrderRequest request, CancellationToken cancellationToken = default)
            => ExecuteAsync(new CreateOrder(orderId, request.CustomerId, request.Currency), cancellationToken);

        public Task<Either<Error, OrderCommandResult>> AddItemAsync(Guid orderId, AddOrderItemRequest request, CancellationToken cancellationToken = default)
            => ExecuteAsync(new AddOrderItem(orderId, request.ProductId, request.UnitPrice, request.Quantity), cancellationToken);

        public Task<Either<Error, OrderCommandResult>> RemoveItemAsync(Guid orderId, RemoveOrderItemRequest request, CancellationToken cancellationToken = default)
            => ExecuteAsync(new RemoveOrderItem(orderId, request.ProductId, request.Quantity), cancellationToken);

        public Task<Either<Error, OrderCommandResult>> ConfirmAsync(Guid orderId, CancellationToken cancellationToken = default)
            => ExecuteAsync(new ConfirmOrder(orderId), cancellationToken);

        public Task<Either<Error, OrderCommandResult>> CancelAsync(Guid orderId, CancelOrderRequest request, CancellationToken cancellationToken = default)
            => ExecuteAsync(new CancelOrder(orderId, request.Reason), cancellationToken);

        private async Task<Either<Error, OrderCommandResult>> ExecuteAsync(OrderCommand command, CancellationToken cancellationToken)
        {
            var metadata = BuildMetadata();
            var appendResult = await _orderCommandService.HandleAsync(command, metadata, cancellationToken);
            if (appendResult.IsLeft)
            {
                return Left(appendResult.Left);
            }

            await _orderReadModelProjector.ProjectAsync(cancellationToken);

            var streamId = OrderAggregate.GetStreamId(command.OrderId);
            var history = await _eventStore.LoadStreamAsync<OrderEvent>(streamId, cancellationToken: cancellationToken);
            var state = OrderAggregate.Fold(history.Select(x => x.Data));

            return Right<OrderCommandResult>(new OrderCommandResult(
                command.OrderId,
                streamId,
                state.Version - appendResult.Right.Count,
                state.Version,
                appendResult.Right.Select(x => x.GetType().Name).ToArray(),
                state.TotalAmount,
                ToStatus(state)));
        }

        private EventMetadata BuildMetadata()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            return new EventMetadata(
                CorrelationId: httpContext?.TraceIdentifier,
                UserId: httpContext?.User?.Identity?.Name,
                TraceId: System.Diagnostics.Activity.Current?.TraceId.ToString());
        }

        private static string ToStatus(OrderState state)
            => state.IsCancelled ? "Cancelled"
                : state.IsConfirmed ? "Confirmed"
                : state.Exists ? "Draft"
                : "NotCreated";
    }
}
