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
            SettingSoftDelete(builder);

            builder.ToTable("tb_orders")
                .HasKey(p => p.Id);

            builder.Property(p => p.OrderName).HasMaxLength(255)
                .IsRequired();
            builder.Property(p => p.OrderNumber).HasMaxLength(25)
                .IsRequired();

            builder.Property(p => p.CreationTime).IsRequired()
                .HasDefaultValue(DateTimeOffset.Now);
            builder.Property(p => p.UpdationTime);

            builder.Property(p => p.Price)
                .HasColumnType("decimal(5,3)")
                .IsRequired();
        }

        private void SettingSoftDelete(EntityTypeBuilder<Order> builder)
        {
            builder.HasQueryFilter(p => p.IsDelete == false);
        }
    }
}
