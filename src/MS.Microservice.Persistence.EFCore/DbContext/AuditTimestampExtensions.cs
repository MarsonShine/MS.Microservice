using Microsoft.EntityFrameworkCore;
using MS.Microservice.Core.Domain.Entity;

namespace MS.Microservice.Persistence.EFCore.DbContext;

public static class AuditTimestampExtensions
{
    /// <summary>Sets UTC timestamps on changed entities according to the audit interfaces they implement.</summary>
    public static void UpdateAuditTimestamps(this Microsoft.EntityFrameworkCore.DbContext context, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        foreach (var entry in context.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.State == EntityState.Added && entry.Entity is ICreatedAt created) created.CreatedAt = now;
            if (entry.Entity is IUpdatedAt updated) updated.UpdatedAt = now;
        }
    }
}
