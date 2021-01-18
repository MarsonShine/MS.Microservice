using MS.Microservice.Domain;
using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Microservice.MongoDb.Test.Entity
{
    public class Person : BaseEntity
    {
        public Person(int id) : base(id)
        {
        }

        public string Name { get; set; } = null!;
    }
}
