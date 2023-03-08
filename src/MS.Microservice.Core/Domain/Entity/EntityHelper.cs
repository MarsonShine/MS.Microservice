using MS.Microservice.Core.Reflection;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MS.Microservice.Core.Domain.Entity
{
    public class EntityHelper
    {
        public static bool EntityEquals(IEntity entity1, IEntity entity2)
        {
            if (entity1 == null || entity2 == null)
            {
                return false;
            }

            if (Object.ReferenceEquals(entity1, entity2))
            {
                return true;
            }

            var typeOfEntity1 = entity1.GetType();
            var typeOfEntity2 = entity2.GetType();
            if (!typeOfEntity1.IsAssignableFrom(typeOfEntity2) && !typeOfEntity2.IsAssignableFrom(typeOfEntity1))
            {
                return false;
            }

            if (HasDefaultKeys(entity1) && HasDefaultKeys(entity2))
            {
                return false;
            }

            var entity1Keys = entity1.GetKeys();
            var entity2Keys = entity2.GetKeys();

            if (entity1Keys.Length != entity2Keys.Length)
            {
                return false;
            }

            for (var i = 0; i < entity1Keys.Length; i++)
            {
                var entity1Key = entity1Keys[i];
                var entity2Key = entity2Keys[i];

                if (entity1Key == null)
                {
                    if (entity2Key == null)
                    {
                        //Both null, so considered as equals
                        continue;
                    }

                    //entity2Key is not null!
                    return false;
                }

                if (entity2Key == null)
                {
                    //entity1Key was not null!
                    return false;
                }

                if (TypeHelper.IsDefaultValue(entity1Key) && TypeHelper.IsDefaultValue(entity2Key))
                {
                    return false;
                }

                if (!entity1Key!.Equals(entity2Key))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool HasDefaultKeys([NotNull] IEntity entity)
        {
            Check.NotNull(entity, nameof(entity));

            foreach (var key in entity.GetKeys())
            {
                if (!IsDefaultKeyValue(key))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsDefaultKeyValue(object value)
        {
            if (value == null)
            {
                return true;
            }

            var type = value.GetType();

            //Workaround for EF Core since it sets int/long to min value when attaching to DbContext
            if (type == typeof(int))
            {
                return Convert.ToInt32(value) <= 0;
            }

            if (type == typeof(long))
            {
                return Convert.ToInt64(value) <= 0;
            }

            return TypeHelper.IsDefaultValue(value);
        }


        public static bool HasDefaultId<TKey>(IEntity<TKey> entity)
        {
            if (EqualityComparer<TKey>.Default.Equals(entity.Id, default))
            {
                return true;
            }

            //Workaround for EF Core since it sets int/long to min value when attaching to dbcontext
            if (typeof(TKey) == typeof(int))
            {
                return Convert.ToInt32(entity.Id) <= 0;
            }

            if (typeof(TKey) == typeof(long))
            {
                return Convert.ToInt64(entity.Id) <= 0;
            }

            return false;
        }

    }
}
