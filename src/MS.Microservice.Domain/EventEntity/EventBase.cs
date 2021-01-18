using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Microservice.Domain
{
    public class EventBase
    {
        public EventBase()
        {
            Id = Guid.NewGuid();
            CreationDate = DateTime.UtcNow;
        }

        public Guid Id { get; private set; }
        public DateTime CreationDate { get; private set; }
    }
}
