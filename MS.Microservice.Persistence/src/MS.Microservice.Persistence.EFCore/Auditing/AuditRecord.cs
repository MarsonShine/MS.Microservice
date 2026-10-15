using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MS.Microservice.Core.Auditing;

namespace MS.Microservice.Persistence.EFCore.Auditing;

public enum AuditChangeKind { Created, Updated, Deleted }

/// <summary>One entity change in a saved operation; multiple records share OperationId and SaveSequence.</summary>
public sealed class AuditRecord
{
    public Guid Id { get; private set; }
    public Guid OperationId { get; private set; }
    public int SaveSequence { get; private set; }
    public string ScopeHash { get; private set; } = "";
    public string Actor { get; private set; } = "";
    public string Action { get; private set; } = "";
    public string? CorrelationId { get; private set; }
    public string ResourceType { get; private set; } = "";
    public string ResourceId { get; private set; } = "";
    public AuditChangeKind Kind { get; private set; }
    public long OccurredAtUtcTicks { get; private set; }
    public string ChangesJson { get; private set; } = "";
    private AuditRecord() { }
    internal AuditRecord(AuditOperationInfo operation, string scopeHash, int sequence, string resourceType, string resourceId,
        AuditChangeKind kind, long utcTicks, string changesJson)
    {
        Id = Guid.NewGuid(); OperationId = operation.Id; SaveSequence = sequence;
        ScopeHash = scopeHash; Actor = operation.Actor; Action = operation.Name;
        CorrelationId = operation.CorrelationId; ResourceType = resourceType; ResourceId = resourceId;
        Kind = kind; OccurredAtUtcTicks = utcTicks; ChangesJson = changesJson;
    }
    internal static string HashScope(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (scope.Length > 256) throw new ArgumentOutOfRangeException(nameof(scope));
        return Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(scope)));
    }
}

public static class AuditRecordModelExtensions
{
    public static ModelBuilder AddAuditRecords(this ModelBuilder model, string? schema = null)
    {
        var entity = model.Entity<AuditRecord>();
        entity.ToTable("AuditRecords", schema);
        entity.HasKey(record => record.Id);
        entity.Property(record => record.Id).ValueGeneratedNever();
        entity.Property(record => record.ScopeHash).HasMaxLength(64).IsUnicode(false);
        entity.Property(record => record.Actor).HasMaxLength(255);
        entity.Property(record => record.Action).HasMaxLength(128);
        entity.Property(record => record.CorrelationId).HasMaxLength(256);
        entity.Property(record => record.ResourceType).HasMaxLength(96).IsUnicode(false);
        entity.Property(record => record.ResourceId).HasMaxLength(256);
        entity.HasIndex(record => new { record.ScopeHash, record.OperationId, record.SaveSequence });
        entity.HasIndex(record => new { record.ScopeHash, record.ResourceType, record.ResourceId, record.OccurredAtUtcTicks, record.Id });
        return model;
    }

    /// <summary>Filters audit reads using the host-authorized scope; it does not infer authorization.</summary>
    public static IQueryable<AuditRecord> ForAuditScope(this Microsoft.EntityFrameworkCore.DbContext context, string scope)
    {
        var hash = AuditRecord.HashScope(scope);
        return context.Set<AuditRecord>().AsNoTracking().Where(record => record.ScopeHash == hash);
    }
}
