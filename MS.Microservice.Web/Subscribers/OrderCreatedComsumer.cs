namespace MS.Microservice.Web.Subscribers
{
    using MassTransit;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using MS.Microservice.IntegrateEvent.Contracts;
    using System.Threading.Tasks;
    public class OrderCreatedComsumer : IConsumer<IOrderCreatedEvent>
    {
        //与业务没有直接关系的应该属性注入,在不注入的情况下，也不会影响程序运行
        //比如日志系统
        private readonly IBus _bus;
        public OrderCreatedComsumer(IBus bus)
        {
            _bus = bus;
            Logger = NullLogger.Instance;
        }

        public ILogger Logger
        {
            get;
            set;
        }

        public async Task Consume(ConsumeContext<IOrderCreatedEvent> context)
        {
            Logger.LogInformation($"集成事件-{nameof(context)} 接受成功，value = " + Newtonsoft.Json.JsonConvert.SerializeObject(context.Message, Newtonsoft.Json.Formatting.Indented));
            //这里继续发送集成事件
            await Task.CompletedTask;
        }
    }
}
