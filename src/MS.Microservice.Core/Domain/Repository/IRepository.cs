using MS.Microservice.Core.Domain.Entity;

namespace MS.Microservice.Core.Domain.Repository
{
    public interface IRepository
    {
        IUnitOfWork UnitOfWork { get; }
    }

    public interface IRepository<TEntity> : IRepository
        where TEntity : IAggregateRoot
    {

    }

    public interface IRepository<TEntity, TKey> : IRepository<TEntity>
        where TEntity : class, IEntity<TKey>, IAggregateRoot
    {

    }
}