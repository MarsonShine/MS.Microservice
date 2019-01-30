using MassTransit;
using Microsoft.Extensions.Logging;
using MS.Microservice.IntegrateEvent.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MS.Microservice.Web.Subscribers
{
    public class OrderCreatedComsumer : IConsumer<IOrderCreatedEvent>
    {
        private readonly ILogger _logger;
        private readonly IBus _bus;
        public OrderCreatedComsumer(ILoggerFactory loggerFactory,IBus bus) {
            _logger = loggerFactory.CreateLogger(nameof(OrderCreatedComsumer));
            _bus = bus;
        }
        public async Task Consume(ConsumeContext<IOrderCreatedEvent> context)
        {
            _logger.LogInformation($"集成事件-{nameof(context)} 接受成功，value = " + Newtonsoft.Json.JsonConvert.SerializeObject(context.Message, Newtonsoft.Json.Formatting.Indented));
            //这里继续发送集成事件
            await Task.CompletedTask;
        }
    }
}
