using Microsoft.EntityFrameworkCore;

namespace MS.Microservice.Idempotency.EFCore;

public static class IdempotencyModel
{
    /// <summary>Adds the idempotency record to the business DbContext so both use the same transaction.</summary>
    public static ModelBuilder AddHttpIdempotency(this ModelBuilder model, string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Entity<IdempotencyRecord>(entry =>
        {
            entry.ToTable("HttpIdempotency", schema);
            entry.HasKey(x => new { x.ScopeHash, x.KeyHash });
            entry.Property(x => x.ScopeHash).HasMaxLength(64).IsRequired();
            entry.Property(x => x.KeyHash).HasMaxLength(64).IsRequired();
            entry.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
            entry.Property(x => x.ContentType).HasMaxLength(128);
            entry.Property(x => x.Location).HasMaxLength(2048);
            entry.HasIndex(x => x.ExpiresAtUtcTicks);
        });
        return model;
    }
}

/// <summary>
/// EF model entity used by <see cref="IdempotencyModel.AddHttpIdempotency"/>.
/// The type must be public because compiled models are generated in the consuming DbContext assembly.
/// </summary>
public sealed class IdempotencyRecord
{
    public string ScopeHash { get; set; } = "";
    public string KeyHash { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public long ExpiresAtUtcTicks { get; set; }
    public long? CompletedAtUtcTicks { get; set; }
    public int? StatusCode { get; set; }
    public string? ContentType { get; set; }
    public string? Location { get; set; }
    public byte[]? Body { get; set; }
}
