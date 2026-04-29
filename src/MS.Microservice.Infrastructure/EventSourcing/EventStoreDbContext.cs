using Microsoft.EntityFrameworkCore;
using System;

namespace MS.Microservice.Infrastructure.EventSourcing
{
    public class EventStoreDbContext : DbContext
    {
        public const string DefaultSchema = "event_sourcing";

        public EventStoreDbContext(DbContextOptions<EventStoreDbContext> options)
            : base(options)
        {
        }

        public DbSet<EventStoreRecord> EventStore => Set<EventStoreRecord>();

        public DbSet<SnapshotRecord> Snapshots => Set<SnapshotRecord>();

        public DbSet<ProjectionCheckpointRecord> ProjectionCheckpoints => Set<ProjectionCheckpointRecord>();

        public DbSet<OrderReadModel> OrderReadModels => Set<OrderReadModel>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema(DefaultSchema);

            modelBuilder.Entity<EventStoreRecord>(entity =>
            {
                entity.ToTable("event_store");
                entity.HasKey(x => x.GlobalPosition);
                entity.Property(x => x.GlobalPosition).ValueGeneratedOnAdd();
                entity.Property(x => x.StreamId).HasMaxLength(200).IsRequired();
                entity.Property(x => x.StreamType).HasMaxLength(100).IsRequired();
                entity.Property(x => x.EventType).HasMaxLength(200).IsRequired();
                entity.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
                entity.Property(x => x.Metadata).HasColumnType("jsonb").IsRequired();
                entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
                entity.HasIndex(x => x.EventId).IsUnique();
                entity.HasIndex(x => new { x.StreamId, x.Version }).IsUnique();
                entity.HasIndex(x => x.StreamId);
                entity.HasIndex(x => x.StreamType);
                entity.HasIndex(x => x.CreatedAt);
            });

            modelBuilder.Entity<SnapshotRecord>(entity =>
            {
                entity.ToTable("snapshots");
                entity.HasKey(x => x.StreamId);
                entity.Property(x => x.StreamId).HasMaxLength(200);
                entity.Property(x => x.StreamType).HasMaxLength(100).IsRequired();
                entity.Property(x => x.State).HasColumnType("jsonb").IsRequired();
                entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
            });

            modelBuilder.Entity<ProjectionCheckpointRecord>(entity =>
            {
                entity.ToTable("projection_checkpoint");
                entity.HasKey(x => x.ProjectionName);
                entity.Property(x => x.ProjectionName).HasMaxLength(200);
                entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
            });

            modelBuilder.Entity<OrderReadModel>(entity =>
            {
                entity.ToTable("order_read_model");
                entity.HasKey(x => x.OrderId);
                entity.Property(x => x.OrderId).HasMaxLength(200);
                entity.Property(x => x.CustomerId).HasMaxLength(200).IsRequired();
                entity.Property(x => x.Currency).HasMaxLength(20).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
                entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
                entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
            });
        }
    }

    public sealed class EventStoreRecord
    {
        public long GlobalPosition { get; set; }

        public Guid EventId { get; set; }

        public string StreamId { get; set; } = string.Empty;

        public string StreamType { get; set; } = string.Empty;

        public int Version { get; set; }

        public string EventType { get; set; } = string.Empty;

        public string Payload { get; set; } = "{}";

        public string Metadata { get; set; } = "{}";

        public DateTimeOffset CreatedAt { get; set; }
    }

    public sealed class SnapshotRecord
    {
        public string StreamId { get; set; } = string.Empty;

        public string StreamType { get; set; } = string.Empty;

        public int Version { get; set; }

        public string State { get; set; } = "{}";

        public DateTimeOffset CreatedAt { get; set; }
    }

    public sealed class ProjectionCheckpointRecord
    {
        public string ProjectionName { get; set; } = string.Empty;

        public long LastGlobalPosition { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }

    public sealed class OrderReadModel
    {
        public string OrderId { get; set; } = string.Empty;

        public string CustomerId { get; set; } = string.Empty;

        public string Currency { get; set; } = string.Empty;

        public string Status { get; set; } = "Draft";

        public int ItemCount { get; set; }

        public decimal TotalAmount { get; set; }

        public int Version { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }
}
