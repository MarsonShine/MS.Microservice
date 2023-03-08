using MediatR;
using MS.Microservice.Core.Domain.Entity;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MS.Microservice.Domain
{
    [Serializable]
    public abstract class Entity : IEntity
    {
        public abstract object[] GetKeys();
        public bool EntityEquals(IEntity other)
        {
            return EntityHelper.EntityEquals(this, other);
        }

        [AllowNull]
        private List<INotification> _domainEvents;
        [JsonIgnore]
        public IReadOnlyCollection<INotification> DomainEvents => _domainEvents?.AsReadOnly()!;

        public void AddDomainEvent(INotification eventItem)
        {
            _domainEvents ??= new List<INotification>();
            _domainEvents.Add(eventItem);
        }

        public void RemoveDomainEvent(INotification eventItem)
        {
            _domainEvents?.Remove(eventItem);
        }

        public void ClearDomainEvents()
        {
            _domainEvents?.Clear();
        }
    }

    [Serializable]
    public abstract class Entity<TId> : Entity, IEntity<TId>
    {
        [AllowNull]
        private TId _id;
        [NotNull]
        public virtual TId Id
        {
            get
            {
                return _id!;
            }
            set {
                _id = value;
            }
        }

        public override object[] GetKeys()
        {
            return new object[] { Id };
        }
    }
}
