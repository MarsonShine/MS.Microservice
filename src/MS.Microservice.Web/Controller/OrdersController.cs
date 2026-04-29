using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Core.Dto;
using MS.Microservice.Web.Application.Models.Orders;
using MS.Microservice.Web.Application.Orders;
using System.Net;

namespace MS.Microservice.Web.Controller
{
    /// <summary>
    /// 订单事件溯源示例控制器。
    /// 用于演示从 Controller -> Application -> Domain(Core) -> Infrastructure(PostgreSQL) 的完整调用链。
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    public sealed class OrdersController : ControllerBase
    {
        private readonly IOrderWorkflowAppService _workflowAppService;
        private readonly IOrderQueryAppService _queryAppService;

        public OrdersController(IOrderWorkflowAppService workflowAppService, IOrderQueryAppService queryAppService)
        {
            _workflowAppService = workflowAppService;
            _queryAppService = queryAppService;
        }

        /// <summary>
        /// 创建订单并追加 <c>OrderCreated</c> 事件。
        /// </summary>
        [HttpPost("{orderId:guid}/create")]
        [ProducesResponseType(typeof(ResultDto<OrderCommandResult>), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> Create(Guid orderId, [FromBody] CreateOrderRequest request)
            => Ok(await ExecuteAsync(() => _workflowAppService.CreateAsync(orderId, request, HttpContext.RequestAborted)));

        /// <summary>
        /// 向订单追加 <c>OrderItemAdded</c> 事件。
        /// </summary>
        [HttpPost("{orderId:guid}/items/add")]
        [ProducesResponseType(typeof(ResultDto<OrderCommandResult>), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> AddItem(Guid orderId, [FromBody] AddOrderItemRequest request)
            => Ok(await ExecuteAsync(() => _workflowAppService.AddItemAsync(orderId, request, HttpContext.RequestAborted)));

        /// <summary>
        /// 向订单追加 <c>OrderItemRemoved</c> 事件。
        /// </summary>
        [HttpPost("{orderId:guid}/items/remove")]
        [ProducesResponseType(typeof(ResultDto<OrderCommandResult>), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> RemoveItem(Guid orderId, [FromBody] RemoveOrderItemRequest request)
            => Ok(await ExecuteAsync(() => _workflowAppService.RemoveItemAsync(orderId, request, HttpContext.RequestAborted)));

        /// <summary>
        /// 确认订单并追加 <c>OrderConfirmed</c> 事件。
        /// </summary>
        [HttpPost("{orderId:guid}/confirm")]
        [ProducesResponseType(typeof(ResultDto<OrderCommandResult>), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> Confirm(Guid orderId)
            => Ok(await ExecuteAsync(() => _workflowAppService.ConfirmAsync(orderId, HttpContext.RequestAborted)));

        /// <summary>
        /// 取消订单并追加 <c>OrderCancelled</c> 事件。
        /// </summary>
        [HttpPost("{orderId:guid}/cancel")]
        [ProducesResponseType(typeof(ResultDto<OrderCommandResult>), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> Cancel(Guid orderId, [FromBody] CancelOrderRequest request)
            => Ok(await ExecuteAsync(() => _workflowAppService.CancelAsync(orderId, request, HttpContext.RequestAborted)));

        /// <summary>
        /// 查询订单详情：同时返回事件流重放结果与投影后的读模型信息。
        /// </summary>
        [HttpGet("{orderId:guid}")]
        [ProducesResponseType(typeof(ResultDto<OrderDetailsResponse>), (int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        public async Task<IActionResult> Get(Guid orderId)
        {
            var order = await _queryAppService.GetAsync(orderId, HttpContext.RequestAborted);
            if (order is null)
            {
                return NotFound(new ResultDto($"订单 {orderId} 不存在。", 404));
            }

            return Ok(new ResultDto<OrderDetailsResponse>(order));
        }

        private static async Task<ResultDto<OrderCommandResult>> ExecuteAsync(Func<Task<MS.Microservice.Core.Functional.Either<MS.Microservice.Core.Functional.Error, OrderCommandResult>>> action)
        {
            var result = await action();
            return result.Match(
                left: error => new ResultDto<OrderCommandResult>(default!, false, error.ToDisplayMessage(), 200),
                right: success => new ResultDto<OrderCommandResult>(success, true, string.Empty, 200));
        }
    }
}
