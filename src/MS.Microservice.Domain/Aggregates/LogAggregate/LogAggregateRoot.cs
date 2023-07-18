using MS.Microservice.Core;
using MS.Microservice.Core.Domain;
using MS.Microservice.Core.Domain.Entity;
using System;

namespace MS.Microservice.Domain.Aggregates.LogAggregate
{
    public class LogAggregateRoot : EntityBase<long>, IAggregateRoot, ICreatedAt, ICreator<int>
    {
        protected LogAggregateRoot() { }
        public LogAggregateRoot(string eventName, string methodName, LogEventTypeEnum type, string description, string content, int creatorId, string ip, string telephone)
        {
            EventName = eventName;
            MethodName = methodName;
            Type = type;
            Description = description;
            Content = content;
            CreatedAt = DateTime.Now;
            CreatorId = creatorId;
            IP = ip;
            Telephone = telephone;
        }

        public string? EventName { get; private set; }
        public LogEventTypeEnum Type { get; private set; }
        public string? MethodName { get; set; }
        public string? Description { get; private set; }
        public string? Content { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CreatorId { get; private set; }
        public string? IP { get; private set; }
        public string? Telephone { get; private set; }
    }
}
