namespace MS.Microservice.IntegrateEvent.Contracts
{
    using System;
    //MassTransit 推荐最佳实践，Event最好使用只读属性
    public interface IOrderCreatedEvent : IIntegrateEvent
    {
        string OrderNumber { get; }
        string OrderName { get; }
        DateTime CreationTime { get; }
    }
}
