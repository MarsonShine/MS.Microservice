using AutoMapper;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using MS.Microservice.Domain;
using MS.Microservice.IntegrateEvent.Consumers;
using MS.Microservice.IntegrateEvent.Contracts;
using MS.Microservice.Web.Handlers.Integrates;
using MS.Microservice.Web.Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Web.Apps.Commands
{
    public class CreateOrderCmd : IRequest
    {
        public string OrderName { get; set; }
        public decimal Price { get; set; }
    }

    public class CreateOrderHandler : IRequestHandler<CreateOrderCmd, Unit>
    {
        private readonly IOrderRepository _orderRepository;
        private readonly ILogger _logger;
        private readonly IMapper _mapper;
        private readonly IBus _bus;
        public CreateOrderHandler(IOrderRepository orderRepository, ILoggerFactory logger,
            IMapper mapper,IBus bus)
        {
            _bus = bus;
            _orderRepository = orderRepository;
            _logger = logger.CreateLogger<CreateOrderHandler>();
            _mapper = mapper;
        }
        public async Task<Unit> Handle(CreateOrderCmd request, CancellationToken cancellationToken)
        {
            //这里应该有验证
            _logger.LogDebug($"{nameof(request)}验证通过");
            var order = _mapper.Map<Order>(request);
            await _orderRepository.AddAsync(order);
            _logger.LogDebug($"订单添加成功");
            _logger.LogDebug("开始发送事件：" + nameof(OrderCreatedEvent));
            
            await _bus.Publish<IOrderCreatedEvent>(new OrderCreatedEvent(order.OrderNumber, order.OrderName));
            return Unit.Value;
        }
    }
}
