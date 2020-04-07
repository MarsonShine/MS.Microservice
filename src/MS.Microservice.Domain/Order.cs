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
        public DateTime? UpdationTime { get; set; }
        public Order()
        {
            CreationTime = DateTime.UtcNow;
        }
        public new void SetID(int id)
        {
            base.SetID(id);
        }
    }
}
