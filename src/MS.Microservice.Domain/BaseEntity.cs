using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Microservice.Domain
{
    public abstract class BaseEntity : IAggregateRoot
    {
        private int id;
        public virtual int Id
        {
            get
            {
                return id;
            }
        }

        private List<INotification> _domainEvents;
        public IReadOnlyCollection<INotification> DomainEvents => _domainEvents?.AsReadOnly();

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

        private bool isDelete;
        public virtual bool IsDelete
        {
            get { return isDelete; }
            protected set { isDelete = value; }
        }
        //逻辑删除
        public void Delete()
        {
            this.IsDelete = true;
        }

        protected void SetID(int id)
        {
            this.id = id;
        }
        private DateTimeOffset creationTime;
        public virtual DateTimeOffset CreationTime
        {
            get { return creationTime; }
            protected set { creationTime = value; }
        }
    }
}
