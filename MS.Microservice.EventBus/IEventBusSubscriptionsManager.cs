using MS.Microservice.EventBus.Abstractions;
using MS.Microservice.EventBus.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.EventBus
{
    /// <summary>
    /// 管理事件
    /// </summary>
    public interface IEventBusSubscriptionsManager
    {
        void AddSubscription<T, TH>()
           where T : IntegrationEvent
           where TH : IIntegrationEventHandler<T>;

        void RemoveSubscription<T, TH>()
             where TH : IIntegrationEventHandler<T>
             where T : IntegrationEvent;
        Type GetEventTypeByName(string eventName);
        void Clear();
        string GetEventKey<T>();
    }
}
