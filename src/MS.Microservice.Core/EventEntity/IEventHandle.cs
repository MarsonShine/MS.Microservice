using System.Threading.Tasks;

namespace MS.Microservice.Core.EventEntity
{
    public interface IEventHandle<in TEvent> 
        where TEvent : EventBase
    {
        Task Handle(TEvent @event);
    }
}