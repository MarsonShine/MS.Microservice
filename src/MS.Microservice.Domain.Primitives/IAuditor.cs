namespace MS.Microservice.Core.Domain.Entity
{
    public interface ICreatedAndUpdatedAt : ICreatedAt, IUpdatedAt
    {
    }

    public interface ICreatorAndUpdater<TId> : ICreator<TId>, IUpdater<TId>
    {
    }

    [Obsolete("Use ICreatorAndUpdater<TId> instead. This compatibility interface will be removed in a future major version.")]
    public interface ICreatorAndUpdator<TId> : ICreatorAndUpdater<TId>, IUpdator<TId>
    {
    }

#pragma warning disable CS0618
    public interface IFullAuditTracker<TAuditorId> : ICreatedAndUpdatedAt, ISoftDeleted, ICreatorAndUpdator<TAuditorId>
    {

    }
#pragma warning restore CS0618
}
