using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MS.Microservice.Domain.Events;

namespace MS.Microservice.Persistence.EFCore.EntityConfigurations;

internal sealed class OutboxMessageEntityTypeConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(message => message.MessageId);
        builder.Property(message => message.MessageId).ValueGeneratedNever();
        builder.Property(message => message.MessageType).HasMaxLength(500).IsRequired();
        builder.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(message => message.OccurredAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(message => message.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(message => message.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(4000);
        builder.Property(message => message.NextAttemptAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(message => message.LockedUntilUtc).HasColumnType("timestamp with time zone");
        builder.Property(message => message.LastAttemptAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(message => message.PublishedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(message => message.TraceId).HasMaxLength(64);
        builder.Property(message => message.CorrelationId).HasMaxLength(100);
        builder.Property(message => message.TraceParent).HasMaxLength(128);
        builder.Property(message => message.TraceState).HasMaxLength(512);

        builder.HasIndex(message => new
        {
            message.Status,
            message.NextAttemptAtUtc,
            message.OccurredAtUtc
        });
        builder.HasIndex(message => message.LockedUntilUtc);
        builder.HasIndex(message => message.MessageType);
    }
}
