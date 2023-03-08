using MS.Microservice.Core.Domain.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System;
using System.Linq.Expressions;
using System.Reflection;

namespace MS.Microservice.Infrastructure.DbContext
{
    public static class SoftDeletedQueryExtensions
    {
        public static void AddSoftDeletedQueryFilter(
        this IMutableEntityType entityData)
        {
            var methodToCall = typeof(SoftDeletedQueryExtensions)?
                .GetMethod(nameof(GetSoftDeleteFilter),
                    BindingFlags.NonPublic | BindingFlags.Static)?
                .MakeGenericMethod(entityData.ClrType);
            var filter = methodToCall?.Invoke(null, Array.Empty<object>());

            entityData.SetQueryFilter((LambdaExpression)filter!);
            entityData.AddIndex(entityData.
                 FindProperty(nameof(ISoftDeleted.DeletedAt)));
        }

        private static LambdaExpression GetSoftDeleteFilter<TEntity>()
            where TEntity : class, ISoftDeleted
        {
            Expression<Func<TEntity, bool>> filter = x => x.DeletedAt == null;
            return filter;
        }
    }
}
