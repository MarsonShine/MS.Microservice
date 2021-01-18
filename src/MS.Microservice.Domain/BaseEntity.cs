using MediatR;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

// 与 MS.Microservice.Core 领域相关整合到 Domain 层下
namespace MS.Microservice.Domain
{
    public abstract class BaseEntity : BaseEntity<int>
    {
        protected BaseEntity(int id) : base(id)
        {
        }
    }

    public abstract class BaseEntity<TKey> : IEntity<TKey>, IAggregateRoot
    {
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。
        private TKey id;
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。
        public virtual TKey Id
        {
            get
            {
                return id;
            }
        }

        protected BaseEntity(TKey id)
        {
            this.id = id;
        }

        private List<INotification>? _domainEvents;
        public IReadOnlyCollection<INotification>? DomainEvents => _domainEvents?.AsReadOnly();

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

        private DateTimeOffset creationTime;
        public virtual DateTimeOffset CreationTime
        {
            get { return creationTime; }
            protected set { creationTime = value; }
        }
    }
}
