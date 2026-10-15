using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using MS.Microservice.Core.Auditing;

namespace MS.Microservice.Persistence.EFCore.Auditing;

/// <summary>Collects opted-in scalar changes once per save and persists them with the business changes.</summary>
/// <remarks>Use known keys and normal accepting saves. Do not mutate audited entities in later save callbacks.</remarks>
public abstract class AuditedDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    private static readonly ConditionalWeakTable<IModel, Dictionary<IEntityType, Rule>> Rules = new();
    private readonly AuditOperationContext _operation;
    private readonly AuditCollectionOptions _limits;
    private readonly TimeProvider _clock;
    private readonly ConditionalWeakTable<object, object> _knownOriginals = new();
    private static readonly object KnownOriginal = new();
    private bool _saving;
    public bool AuditingEnabled { get; set; } = true;

    protected AuditedDbContext(DbContextOptions options, AuditOperationContext operation,
        AuditCollectionOptions? limits = null, TimeProvider? clock = null) : base(options)
    {
        _operation = operation ?? throw new ArgumentNullException(nameof(operation));
        _limits = limits ?? new(); _clock = clock ?? TimeProvider.System;
        if (_limits.MaximumRecordBytes is < 64 or > 1_048_576 || _limits.MaximumRecordsPerSave is < 1 or > 10_000
            || _limits.MaximumBatchBytes < _limits.MaximumRecordBytes)
            throw new ArgumentOutOfRangeException(nameof(limits));
        ChangeTracker.Tracked += (_, args) =>
        {
            _knownOriginals.Remove(args.Entry.Entity);
            if (AuditingEnabled && args.FromQuery && Plan.ContainsKey(args.Entry.Metadata))
                _knownOriginals.Add(args.Entry.Entity, KnownOriginal);
        };
        ChangeTracker.StateChanged += (_, args) =>
        {
            if (args.NewState == EntityState.Detached) _knownOriginals.Remove(args.Entry.Entity);
        };
    }
    private Dictionary<IEntityType, Rule> Plan => Rules.GetValue(Model, BuildRules);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (!AuditingEnabled) return base.SaveChanges(acceptAllChangesOnSuccess);
        using var save = Prepare(acceptAllChangesOnSuccess, CancellationToken.None);
        try { var result = base.SaveChanges(acceptAllChangesOnSuccess); save.Accept(); return result; }
        catch { save.RemovePending(); throw; }
    }
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (!AuditingEnabled) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        using var save = Prepare(acceptAllChangesOnSuccess, cancellationToken);
        try { var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); save.Accept(); return result; }
        catch { save.RemovePending(); throw; }
    }

    private SaveBatch Prepare(bool acceptChanges, CancellationToken token)
    {
        if (_saving) throw new InvalidOperationException("An audited context cannot save recursively or concurrently.");
        _saving = true;
        var batch = new SaveBatch(this, ChangeTracker.AutoDetectChangesEnabled);
        try
        {
            token.ThrowIfCancellationRequested();
            if (batch.DetectChanges) ChangeTracker.DetectChanges();
            ChangeTracker.AutoDetectChangesEnabled = false;
            var operation = _operation.Current;
            string? scopeHash = null;
            var sequence = 0;
            var bytes = 0L;
            var occurred = _clock.GetUtcNow().UtcTicks;
            var plan = Plan;
            foreach (var entry in ChangeTracker.Entries())
            {
                token.ThrowIfCancellationRequested();
                if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)
                    || !plan.TryGetValue(entry.Metadata, out var rule)) continue;
                var oldKnown = _knownOriginals.TryGetValue(entry.Entity, out _);
                var fields = rule.Fields.Where(property => entry.State != EntityState.Modified
                    || entry.Property(property.Name).IsModified
                    && (!oldKnown || !Equals(entry.Property(property.Name).OriginalValue, entry.Property(property.Name).CurrentValue))).ToArray();
                if (fields.Length == 0) continue;
                if (!acceptChanges) throw new InvalidOperationException("Audited writes require acceptAllChangesOnSuccess=true.");
                if (!Database.IsRelational() || Database.AutoTransactionBehavior == AutoTransactionBehavior.Never)
                    throw new InvalidOperationException("Auditing requires a relational provider with transactional saving enabled.");
                if (operation is null) throw new InvalidOperationException("Begin an audit operation before saving audited changes.");
                if (Model.FindEntityType(typeof(AuditRecord)) is null) throw new InvalidOperationException("Call AddAuditRecords in the host model.");
                if (sequence == 0) { sequence = _operation.NextSaveSequence(); scopeHash = AuditRecord.HashScope(operation.Scope); }
                var resourceId = ResourceId(entry, rule.Keys);
                var json = Changes(entry, fields, oldKnown);
                bytes += Encoding.UTF8.GetByteCount(json);
                if (batch.Records.Count == _limits.MaximumRecordsPerSave || bytes > _limits.MaximumBatchBytes)
                    throw new InvalidOperationException("Audit changes exceed the per-save budget.");
                var kind = entry.State switch { EntityState.Added => AuditChangeKind.Created,
                    EntityState.Deleted => AuditChangeKind.Deleted, _ => AuditChangeKind.Updated };
                batch.Records.Add(new(operation, scopeHash!, sequence, rule.Resource, resourceId, kind, occurred, json));
                batch.Entities.Add(entry.Entity);
            }
            if (batch.Records.Count > 0) Set<AuditRecord>().AddRange(batch.Records);
            return batch;
        }
        catch { batch.RemovePending(); batch.Dispose(); throw; }
    }

    private string Changes(EntityEntry entry, IProperty[] fields, bool oldKnown)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartArray();
        foreach (var property in fields)
        {
            var value = entry.Property(property.Name);
            writer.WriteStartObject(); writer.WriteString("field", property.Name);
            writer.WriteBoolean("beforeKnown", oldKnown && entry.State != EntityState.Added);
            if (oldKnown && entry.State != EntityState.Added)
            { writer.WritePropertyName("before"); AuditScalarJson.Write(writer, value.OriginalValue, _limits.MaximumRecordBytes); }
            if (entry.State != EntityState.Deleted)
            { writer.WritePropertyName("after"); AuditScalarJson.Write(writer, value.CurrentValue, _limits.MaximumRecordBytes); }
            writer.WriteEndObject();
            if (writer.BytesCommitted + writer.BytesPending > _limits.MaximumRecordBytes)
                throw new InvalidOperationException("An audit record exceeds the byte budget.");
        }
        writer.WriteEndArray(); writer.Flush();
        if (buffer.WrittenCount > _limits.MaximumRecordBytes) throw new InvalidOperationException("An audit record exceeds the byte budget.");
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string ResourceId(EntityEntry entry, IProperty[] keys)
    {
        var values = keys.Select(property => entry.Property(property.Name)).ToArray();
        if (values.Any(property => property.IsTemporary || property.CurrentValue is null))
            throw new InvalidOperationException("Audit resource keys must be known before saving; generated temporary keys are unsupported.");
        string result;
        if (values.Length == 1) result = Convert.ToString(values[0].CurrentValue, CultureInfo.InvariantCulture)!;
        else
        {
            var buffer = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(buffer);
            writer.WriteStartObject();
            foreach (var value in values)
            { writer.WritePropertyName(value.Metadata.Name); AuditScalarJson.Write(writer, value.CurrentValue, 1024); }
            writer.WriteEndObject(); writer.Flush(); result = Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
        if (string.IsNullOrWhiteSpace(result) || result.Length > 256)
            throw new InvalidOperationException("Audit resource identity must contain 1–256 characters.");
        _ = new UTF8Encoding(false, true).GetByteCount(result);
        return result;
    }

    private static Dictionary<IEntityType, Rule> BuildRules(IModel model)
    {
        var result = new Dictionary<IEntityType, Rule>();
        foreach (var type in model.GetEntityTypes())
        {
            if (type.FindAnnotation(AuditModelExtensions.FieldsAnnotation)?.Value is not string names) continue;
            if (type.ClrType == typeof(AuditRecord)) throw new InvalidOperationException("Audit records cannot audit themselves.");
            var fields = names.Split('|').Select(name => type.FindProperty(name)
                ?? throw new InvalidOperationException("Audit selection must reference mapped scalar properties: " + name)).ToArray();
            var keys = type.FindPrimaryKey()?.Properties.ToArray() ?? throw new InvalidOperationException("Audited entities require a primary key.");
            if (fields.Any(property => !AuditScalarJson.Supports(property.ClrType) || property.ValueGenerated != ValueGenerated.Never)
                || keys.Any(property => !AuditScalarJson.Supports(property.ClrType)))
                throw new InvalidOperationException("Audit fields must be supported, non-generated scalar properties.");
            result.Add(type, new((string)type.FindAnnotation(AuditModelExtensions.ResourceAnnotation)!.Value!, fields, keys));
        }
        return result;
    }
    private sealed record Rule(string Resource, IProperty[] Fields, IProperty[] Keys);
    private sealed class SaveBatch(AuditedDbContext owner, bool detectChanges) : IDisposable
    {
        internal bool DetectChanges { get; } = detectChanges;
        internal List<AuditRecord> Records { get; } = [];
        internal List<object> Entities { get; } = [];
        internal void Accept()
        {
            if (Records.Any(record => owner.Entry(record).State == EntityState.Added))
                throw new InvalidOperationException("Save suppression is incompatible with collected audit records.");
            foreach (var entity in Entities) owner._knownOriginals.AddOrUpdate(entity, KnownOriginal);
        }
        internal void RemovePending()
        {
            foreach (var record in Records)
                if (owner.Entry(record).State == EntityState.Added) owner.Entry(record).State = EntityState.Detached;
        }
        public void Dispose() { owner.ChangeTracker.AutoDetectChangesEnabled = DetectChanges; owner._saving = false; }
    }
}
