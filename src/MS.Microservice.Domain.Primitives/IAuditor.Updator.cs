using System;

namespace MS.Microservice.Core.Domain.Entity
{
    public interface IUpdater<TId>
    {
        TId UpdaterId { get; }
    }

    [Obsolete("Use IUpdater<TId> instead. This compatibility interface will be removed in a future major version.")]
    public interface IUpdator<TId> : IUpdater<TId>
    {
        TId UpdatorId { get; }

        TId IUpdater<TId>.UpdaterId => UpdatorId;
    }
}
