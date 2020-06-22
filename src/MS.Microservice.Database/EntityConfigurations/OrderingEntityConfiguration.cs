using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MS.Microservice.Domain;
using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Microservice.Database.EntityConfigurations
{
    public class OrderingEntityConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(EntityTypeBuilder<Order> builder)
        {
            
        }
    }
}
