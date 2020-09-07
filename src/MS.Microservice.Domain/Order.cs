using MS.Microservice.Domain.Events;
using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Microservice.Domain
{
    public class Order : BaseEntity, IAggregateRoot
    {
        public string OrderNumber { get; private set; }
        public string OrderName { get; protected set; }
        public decimal Price { get; set; }
        public DateTimeOffset? UpdationTime { get; set; }
        public Order()
        {
            CreationTime = DateTimeOffset.UtcNow;
        }

        public void Remove()
        {
            DomainEvent.Raise(new UpdatingNameEvent(1, "newName")).GetAwaiter()
                .GetResult();
            Delete();
        }

        public Address Address { get; private set; }
    }
}
