using System.Threading.Tasks;

public interface IEventHandler<TEvent> where TEvent : IEvent
{
    Task Handle(TEvent evt);
}