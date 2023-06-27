using System.Threading.Tasks;

namespace MS.Microservice.Core.EventBus
{
    public interface IEventHandler<TEvent> where TEvent : IEvent
    {
        Task Handle(TEvent evt);
    }
}