namespace MS.Microservice.Core.Domain.Entity
{
    public interface ICreatedAndUpdatedAt : ICreatedAt, IUpdatedAt
    {
    }

    public interface ICreatorAndUpdator<TId> : ICreator<TId>, IUpdator<TId>
    {
    }

    public interface IFullAuditTracker<TCreatorAndUpdatorId> : ICreatedAndUpdatedAt, ISoftDeleted, ICreatorAndUpdator<TCreatorAndUpdatorId>
    {

    }
}
