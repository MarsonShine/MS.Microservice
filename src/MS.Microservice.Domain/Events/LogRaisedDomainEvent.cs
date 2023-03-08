using MediatR;
using MS.Microservice.Domain.Aggregates.LogAggregate;

namespace MS.Microservice.Domain.Events
{
    public class LogRaisedDomainEvent : INotification
    {
        public LogRaisedDomainEvent(string name, LogEventTypeEnum eventType, string method, object data, string description)
        {
            Name = name;
            EventType = eventType;
            Method = method;
            Data = data;
            Description = description;
        }

        public string Name { get; }
        public LogEventTypeEnum EventType { get; }
        public string Method { get; }
        public object Data { get; }
        public string Description { get; }
    }
}
