using System;

namespace MS.Microservice.Core.Domain.Entity
{
    public interface IUpdator<TId>
    {
        TId UpdatorId { get; }
    }
}
