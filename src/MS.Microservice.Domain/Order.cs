using MS.Microservice.Domain.Events;
using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Microservice.Domain
{
    public class Order : BaseEntity, IAggregateRoot
    {
        public string OrderNumber { get; private set; } = null!;
        public string OrderName { get; protected set; } = null!;
        public decimal Price { get; set; }
        public DateTimeOffset? UpdationTime { get; set; }
        public Order() : this(0)
        {
           
        }

        public Order(int id) : base(id)
        {
            CreationTime = DateTimeOffset.UtcNow;
        }

        public void Remove()
        {
            DomainEvent.Raise(new UpdatingNameEvent(1, "newName")).GetAwaiter()
                .GetResult();
            Delete();
        }

        public Address? Address { get; private set; }
    }
}
