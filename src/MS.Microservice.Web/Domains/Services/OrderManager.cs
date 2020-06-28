using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MS.Microservice.Repostitory.Contracts;

namespace MS.Microservice.Web.Domains.Services
{
    /// <summary>
    /// 领域服务层，专职处理领域操作和业务规则的
    /// 并且最好是无状态的
    /// </summary>
    public class OrderManager
    {
        private readonly IOrderRepository _orderRepository;
        public OrderManager(IOrderRepository orderRepository)
        {
            _orderRepository = orderRepository;
            Logger = NullLogger.Instance;
        }

        public ILogger Logger { get; set; }
    }
}
