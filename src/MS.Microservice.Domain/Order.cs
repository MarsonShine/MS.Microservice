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

        public void Remove() => Delete();
    }
}
