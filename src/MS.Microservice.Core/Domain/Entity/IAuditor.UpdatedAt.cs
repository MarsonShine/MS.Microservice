using System;

namespace MS.Microservice.Core.Domain.Entity
{
    public interface IUpdatedAt
    {
        DateTime? UpdatedAt { get; set; }
    }
}
