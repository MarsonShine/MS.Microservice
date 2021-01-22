using MS.Microservice.EventBus.Events;
using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Microservice.EventBus.Abstractions
{
    /// <summary>
    /// 定义事件总线的基本操作行为
    /// 可以集成不同的中间件,RabbitMQ,Kafka 等
    /// 这样做到了 OCP,SRP
    /// </summary>
    public interface IEventbus
    {
        void Publish(IntegrationEvent @event);

        void Subscribe<T, TH>()
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>;

        //void SubscribeDynamic<TH>(string eventName)
        //    where TH : IDynamicIntegrationEventHandler;

        //void UnsubscribeDynamic<TH>(string eventName)
        //    where TH : IDynamicIntegrationEventHandler;

        void Unsubscribe<T, TH>()
            where TH : IIntegrationEventHandler<T>
            where T : IntegrationEvent;
    }
}
