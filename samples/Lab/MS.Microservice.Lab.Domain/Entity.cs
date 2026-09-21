using MS.Microservice.Core.Domain.Entity;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MS.Microservice.Domain
{
    /// <summary>
    /// Marker interface for domain events.
    /// </summary>
    public interface IDomainEvent : Core.Messaging.IDomainEvent { }

    [Serializable]
    public abstract class Entity : IEntity, IHasDomainEvents
    {
        private readonly List<IDomainEvent> _domainEvents = [];

        [JsonIgnore]
        public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

        public void AddDomainEvent(IDomainEvent eventItem)
        {
            ArgumentNullException.ThrowIfNull(eventItem);
            _domainEvents.Add(eventItem);
        }

        public void RemoveDomainEvent(IDomainEvent eventItem)
        {
            ArgumentNullException.ThrowIfNull(eventItem);
            _domainEvents.Remove(eventItem);
        }

        public void ClearDomainEvents()
        {
            _domainEvents.Clear();
        }
    }

    [Serializable]
    public abstract class Entity<TId> : Entity, IEntity<TId>
    {
        private TId _id = default!;

        public bool EntityEquals(IEntity<TId> other) => EntityHelper.EntityEquals<TId>(this, other);

        [AllowNull]
        public virtual TId Id
        {
            get
            {
                return _id;
            }
            set
            {
                _id = value!;
            }
        }
    }
}
