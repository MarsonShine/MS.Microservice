using System.Threading.Tasks;

namespace MS.Microservice.Domain
{
    public interface IEventHandle<in TEvent> 
        where TEvent : EventBase
    {
        Task Handle(TEvent @event);
    }
}