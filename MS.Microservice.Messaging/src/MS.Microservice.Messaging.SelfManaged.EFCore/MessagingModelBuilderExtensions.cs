using Microsoft.EntityFrameworkCore;

namespace MS.Microservice.Messaging.SelfManaged;

public static class MessagingModelBuilderExtensions
{
    /// <summary>Maps messaging into the business context. Migrations remain owned by that context's application.</summary>
    public static ModelBuilder AddSelfManagedMessaging(this ModelBuilder modelBuilder, string schema = "messaging")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        var outbox = modelBuilder.Entity<OutboxEntry>();
        outbox.ToTable("Outbox", schema);
        outbox.HasKey(x => x.Id);
        outbox.Property(x => x.Id).ValueGeneratedNever();
        outbox.Property(x => x.ContractName).HasMaxLength(MessageRoutingKey.MaxContractNameLength);
        // Event identity must round-trip 100ns ticks even on databases with microsecond timestamps.
        outbox.Property(x => x.OccurredAtUtc).HasConversion(value => value.Ticks,
            ticks => new DateTime(ticks, DateTimeKind.Utc));
        outbox.Property(x => x.Payload).IsRequired();
        outbox.Property(x => x.LockToken).IsConcurrencyToken();
        outbox.Property(x => x.CorrelationId).HasMaxLength(MessageMetadataLimits.CorrelationIdMaxLength);
        outbox.Property(x => x.TraceParent).HasMaxLength(MessageMetadataLimits.TraceParentMaxLength);
        outbox.Property(x => x.TraceState).HasMaxLength(MessageMetadataLimits.TraceStateMaxLength);
        outbox.Property(x => x.ErrorCode).HasMaxLength(200);
        outbox.HasIndex(x => new { x.State, x.NextAttemptAtUtc });
        outbox.HasIndex(x => x.LockedUntilUtc);
        outbox.HasIndex(x => x.CompletedAtUtc);

        var inbox = modelBuilder.Entity<InboxEntry>();
        inbox.ToTable("Inbox", schema);
        inbox.HasKey(x => new { x.MessageId, x.Consumer });
        inbox.Property(x => x.MessageId).ValueGeneratedNever();
        inbox.Property(x => x.Consumer).HasMaxLength(200);
        inbox.Property(x => x.ContractName).HasMaxLength(MessageRoutingKey.MaxContractNameLength);
        inbox.Property(x => x.OccurredAtUtc).HasConversion(value => value.Ticks,
            ticks => new DateTime(ticks, DateTimeKind.Utc));
        inbox.Property(x => x.Payload).IsRequired();
        inbox.Property(x => x.LockToken).IsConcurrencyToken();
        inbox.Property(x => x.CorrelationId).HasMaxLength(MessageMetadataLimits.CorrelationIdMaxLength);
        inbox.Property(x => x.TraceParent).HasMaxLength(MessageMetadataLimits.TraceParentMaxLength);
        inbox.Property(x => x.TraceState).HasMaxLength(MessageMetadataLimits.TraceStateMaxLength);
        inbox.Property(x => x.ErrorCode).HasMaxLength(200);
        inbox.HasIndex(x => new { x.State, x.NextAttemptAtUtc });
        inbox.HasIndex(x => x.CompletedAtUtc);
        return modelBuilder;
    }
}
