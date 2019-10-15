using AutoMapper;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MS.Microservice.Domain;
using MS.Microservice.IntegrateEvent.Consumers;
using MS.Microservice.IntegrateEvent.Contracts;
using MS.Microservice.Web.Domains.Repositories.Contracts;
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
        private readonly IMapper _mapper;
        private readonly IBus _bus;
        public ILogger Logger { get; set; }
        public CreateOrderHandler(IOrderRepository orderRepository, ILoggerFactory logger,
            IMapper mapper,IBus bus)
        {
            _bus = bus;
            _orderRepository = orderRepository;
            _mapper = mapper;
            Logger = NullLogger.Instance;
        }
        public async Task<Unit> Handle(CreateOrderCmd request, CancellationToken cancellationToken)
        {
            //这里应该有验证
            Logger.LogDebug($"{nameof(request)}验证通过");
            var order = _mapper.Map<Order>(request);
            await _orderRepository.AddAsync(order);
            Logger.LogDebug($"订单添加成功");
            Logger.LogDebug("开始发送事件：" + nameof(OrderCreatedEvent));
            
            await _bus.Publish<IOrderCreatedEvent>(new OrderCreatedEvent(order.OrderNumber, order.OrderName));
            return Unit.Value;
        }
    }
}
