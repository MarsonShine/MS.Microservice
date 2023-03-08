using System;

namespace MS.Microservice.Core.Domain.Entity
{
    public interface ISoftDeleted
    {
        DateTime? DeletedAt { get; }
    }
}
