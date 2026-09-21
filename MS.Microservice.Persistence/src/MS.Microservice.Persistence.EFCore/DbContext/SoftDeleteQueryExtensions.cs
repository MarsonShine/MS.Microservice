using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MS.Microservice.Core.Domain.Entity;

namespace MS.Microservice.Persistence.EFCore.DbContext;

public static class SoftDeletedQueryExtensions
{
    public static EntityTypeBuilder<TEntity> AddSoftDeletedQueryFilter<TEntity>(
        this EntityTypeBuilder<TEntity> entity) where TEntity : class, ISoftDeleted
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.HasQueryFilter(row => row.DeletedAt == null);
        entity.HasIndex(row => row.DeletedAt);
        return entity;
    }
}
