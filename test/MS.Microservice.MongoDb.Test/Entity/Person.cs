using MS.Microservice.Domain;
using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Microservice.MongoDb.Test.Entity
{
    public class Person : BaseEntity
    {
        public string Name { get; set; } = null!;
    }
}
