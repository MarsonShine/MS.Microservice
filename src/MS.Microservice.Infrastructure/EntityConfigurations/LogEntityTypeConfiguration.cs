using MS.Microservice.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using MS.Microservice.Domain.Aggregates.LogAggregate;
using MS.Microservice.Core;

namespace MS.Microservice.Infrastructure.EntityConfigurations
{
    public class LogEntityTypeConfiguration : IEntityTypeConfiguration<LogAggregateRoot>
    {
        public void Configure(EntityTypeBuilder<LogAggregateRoot> builder)
        {
            builder.ToTable("Logs");

            builder.HasKey(p => p.Id);
            builder.Ignore(p => p.DomainEvents);

            // 属性
            builder.Property(p => p.EventName).IsRequired().HasMaxLength(25);
            builder.Property(p => p.MethodName).IsRequired().HasMaxLength(200);
            builder.Property(p => p.IP).IsRequired().HasMaxLength(20);
            builder.Property(p => p.Telephone).HasMaxLength(20);
            builder.Property(p => p.Type)
                .HasConversion(
                    typeEnum => typeEnum.ToString(),
                    typeString => Enum.Parse<LogEventTypeEnum>(typeString)
                )
                .IsRequired()
                .HasMaxLength(25);

            // 索引
            builder.HasIndex(p => p.CreatorId);
            builder.HasIndex(p => p.CreatedAt);
            builder.HasIndex(p => new { p.CreatorId, p.CreatedAt, p.IP, p.MethodName });
        }
    }
}
