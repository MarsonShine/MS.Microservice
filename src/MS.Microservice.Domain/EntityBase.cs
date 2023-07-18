using MS.Microservice.Core.Reflection;
using System;
using System.Diagnostics.CodeAnalysis;

namespace MS.Microservice.Domain
{
    public abstract class EntityBase<TId> : Entity<TId>
    {
        int? _requestedHashCode;

        public virtual bool IsTransient()
        {
            return TypeHelper.IsDefaultValue(this.Id);
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
                if (!_requestedHashCode.HasValue)
                    _requestedHashCode = this.Id.GetHashCode() ^ 31; // XOR for random distribution (http://blogs.msdn.com/b/ericlippert/archive/2011/02/28/guidelines-and-rules-for-gethashcode.aspx)

                return _requestedHashCode.Value;
            }
            else
                return base.GetHashCode();

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
