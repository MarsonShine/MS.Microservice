using MongoDB.Driver;
using MongoDB.Driver.Linq;
using MS.Microservice.Core.Repository;
using MS.Microservice.Domain;

namespace MS.MicroService.MongoDb.Repository
{
    public interface IMongoDbRepository<TEntity> : IRepository<TEntity>
        where TEntity : BaseEntity
    {
        IMongoDatabase Database { get; }

        IMongoCollection<TEntity> Collection { get; }

        IMongoQueryable<TEntity> GetMongoQueryable();
    }

    public interface IMongoDbRepository<TEntity, TKey> : IMongoDbRepository<TEntity>
        where TEntity : BaseEntity
    {

    }
}
