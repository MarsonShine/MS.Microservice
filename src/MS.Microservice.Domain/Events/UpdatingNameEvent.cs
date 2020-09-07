using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Microservice.Domain.Events
{
    public class UpdatingNameEvent : INotification
    {
        public UpdatingNameEvent(int id,string newName)
        {
            Id = id;
            NewName = newName;
        }

        public int Id { get; }
        public string NewName { get; }
    }
}
