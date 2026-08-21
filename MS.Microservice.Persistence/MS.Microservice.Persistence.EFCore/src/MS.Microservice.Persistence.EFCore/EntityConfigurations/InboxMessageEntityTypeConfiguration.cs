using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MS.Microservice.Domain.Events;

namespace MS.Microservice.Persistence.EFCore.EntityConfigurations;

internal sealed class InboxMessageEntityTypeConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("InboxMessages");
        builder.HasKey(message => message.DeduplicationKey);
        builder.Property(message => message.DeduplicationKey).HasMaxLength(300).ValueGeneratedNever();
        builder.Property(message => message.Consumer).HasMaxLength(200).IsRequired();
        builder.Property(message => message.MessageType).HasMaxLength(500);
        builder.Property(message => message.Source).HasMaxLength(200);
        builder.Property(message => message.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(message => message.ReceivedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(message => message.ProcessingStartedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(message => message.ProcessingLeaseExpiresAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(message => message.ProcessedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(message => message.LastDuplicateAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(message => message.LastError).HasMaxLength(4000);
        builder.Property(message => message.TraceId).HasMaxLength(64);
        builder.Property(message => message.CorrelationId).HasMaxLength(100);

        builder.HasIndex(message => new { message.MessageId, message.Consumer }).IsUnique();
        builder.HasIndex(message => new { message.Status, message.ReceivedAtUtc });
        builder.HasIndex(message => message.ProcessingLeaseExpiresAtUtc);
    }
}
