using System;
using System.Collections.Generic;

namespace MS.Microservice.Core.Domain.Entity
{
    public class EntityHelper
    {
        /// <summary>Compares the declared IDs using their key type.</summary>
        /// <remarks>TKey is one logical key value, including a typed composite key.</remarks>
        public static bool EntityEquals<TKey>(IEntity<TKey>? entity1, IEntity<TKey>? entity2)
        {
            if (entity1 is null || entity2 is null) return false;
            if (ReferenceEquals(entity1, entity2)) return true;

            var firstType = entity1.GetType();
            var secondType = entity2.GetType();
            if (!firstType.IsAssignableFrom(secondType) && !secondType.IsAssignableFrom(firstType)) return false;

            var firstId = entity1.Id;
            var secondId = entity2.Id;
            if (IsDefaultId(firstId) && IsDefaultId(secondId)) return false;
            return EqualityComparer<TKey>.Default.Equals(firstId, secondId);
        }

        /// <summary>Checks the declared Id without constructing an object[] key list.</summary>
        public static bool HasDefaultKeys<TKey>(IEntity<TKey> entity) => HasDefaultId(entity);

        public static bool HasDefaultId<TKey>(IEntity<TKey> entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return IsDefaultId(entity.Id);
        }

        private static bool IsDefaultId<TKey>(TKey value)
        {
            if (EqualityComparer<TKey>.Default.Equals(value, default)) return true;

            // Preserve the EF temporary-key rule while keeping the value typed all the way through.
            return (typeof(TKey) == typeof(int) || typeof(TKey) == typeof(long))
                && Comparer<TKey>.Default.Compare(value, default) < 0;
        }
    }
}
