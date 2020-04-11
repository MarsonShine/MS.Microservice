using MS.Microservice.Domain;
using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Microservice.MongoDb.Test.Entity
{
    public class City : BaseEntity
    {
        public string Name { get; set; }
        
        public void SetId(int id)
        {
            SetID(id);
        }
    }
}
