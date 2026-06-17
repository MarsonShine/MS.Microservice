using MS.Microservice.Core.Reflection;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MS.Microservice.Domain
{
    public abstract class EntityBase<TId> : Entity<TId>
    {
        public virtual bool IsTransient()
        {
            return Id is null || TypeHelper.IsDefaultValue(Id);
        }

        public override bool Equals([AllowNull] object obj)
        {
            if (obj == null || obj is not Entity)
                return false;

            if (Object.ReferenceEquals(this, obj))
                return true;

            if (this.GetType() != obj.GetType())
                return false;

            var item = (EntityBase<TId>)obj;

            if (item.IsTransient() || this.IsTransient())
                return false;
            else
                return EntityEquals(item);
        }

        public override int GetHashCode()
        {
            if (!IsTransient())
            {
                var id = Id;
                return HashCode.Combine(GetType(), id is null ? 0 : EqualityComparer<TId>.Default.GetHashCode(id));
            }
            else
            {
                return base.GetHashCode();
            }

        }

        public static bool operator ==(EntityBase<TId>? left, EntityBase<TId>? right)
        {
            if (Equals(left, null))
                return Equals(right, null);
            else
                return left.Equals(right);
        }

        public static bool operator !=(EntityBase<TId>? left, EntityBase<TId>? right)
        {
            return !(left == right);
        }
    }
}
