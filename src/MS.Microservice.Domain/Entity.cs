using MS.Microservice.Core.Domain.Entity;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MS.Microservice.Domain
{
    /// <summary>
    /// Marker interface for domain events (used with Wolverine)
    /// </summary>
    public interface IDomainEvent { }

    [Serializable]
    public abstract class Entity : IEntity
    {
        public abstract object[] GetKeys();
        public bool EntityEquals(IEntity other)
        {
            return EntityHelper.EntityEquals(this, other);
        }

        [AllowNull]
        private List<IDomainEvent> _domainEvents;
        [JsonIgnore]
        public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents?.AsReadOnly()!;

        public void AddDomainEvent(IDomainEvent eventItem)
        {
            _domainEvents ??= new List<IDomainEvent>();
            _domainEvents.Add(eventItem);
        }

        public void RemoveDomainEvent(IDomainEvent eventItem)
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
