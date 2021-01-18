namespace MS.Microservice.Domain
{
    public interface IRepository
    {
    }

    public interface IRepository<TEntity> where TEntity : class, IEntity
    {

    }

    public interface IRepository<TEntity, TKey> : IRepository<TEntity>
        where TEntity : class, IEntity<TKey>
    {

    }
}