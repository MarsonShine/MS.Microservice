using MongoDB.Driver;
using MongoDB.Driver.Linq;
using MS.Microservice.Core.Repository;
using MS.Microservice.Domain;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace MS.MicroService.MongoDb.Repository
{
    public class MongoDbRepository<TMongoDbContext, TEntity> : BasicRepositoryBase<TEntity>
        where TMongoDbContext : IMongoDbContext
        where TEntity : BaseEntity
    {
        public IMongoDatabase Database => DbContext.Database;

        public IMongoCollection<TEntity> Collection => DbContext.Collection<TEntity>();

        public IMongoQueryable<TEntity> GetMongoQueryable() => Collection.AsQueryable();

        public virtual TMongoDbContext DbContext => DbContextProvider.GetDbContext();

        protected IMongoDbContextProvider<TMongoDbContext> DbContextProvider { get; }

        public MongoDbRepository(IMongoDbContextProvider<TMongoDbContext> dbContextProvider)
        {
            DbContextProvider = dbContextProvider;
        }

        public override async Task<bool> DeleteAsync([NotNull] Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var entities = await GetMongoQueryable()
                .Where(predicate)
                .ToListAsync(cancellationToken);

            try
            {
                foreach (var entity in entities)
                {
                    await DeleteAsync(entity, cancellationToken);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override async Task<bool> DeleteAsync([NotNull] TEntity entity, CancellationToken cancellationToken = default)
        {
            var result = await Collection.DeleteOneAsync(CreateEntityFilter(entity), cancellationToken);

            return result.DeletedCount > 0;
        }

        public override async Task<TEntity> FindAsync([NotNull] Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await GetMongoQueryable()
                .Where(predicate)
                .SingleOrDefaultAsync(cancellationToken);
        }

        public override async Task<TEntity> InsertAsync([NotNull] TEntity entity, CancellationToken cancellationToken = default)
        {
            await InsertAsync(entity, null, cancellationToken);
            return entity;
        }

        private async Task InsertAsync([NotNull] TEntity entity, InsertOneOptions options = default, CancellationToken cancellationToken = default)
        {
            await Collection.InsertOneAsync(entity, options, cancellationToken);
        }

        public override async Task<TEntity> UpdateAsync([NotNull] TEntity entity, CancellationToken cancellationToken = default)
        {
            return await ReplaceAsync(CreateEntityFilter(entity), entity, null, cancellationToken);
        }

        private async Task<TEntity> ReplaceAsync(FilterDefinition<TEntity> filter, [NotNull] TEntity entity, ReplaceOptions options = default, CancellationToken cancellationToken = default)
        {
            var result = await Collection.ReplaceOneAsync(filter, entity, options, cancellationToken);

            return result.IsAcknowledged ? entity : null;
        }

        protected virtual FilterDefinition<TEntity> CreateEntityFilter(TEntity entity)
        {
            return Builders<TEntity>.Filter.And(
                Builders<TEntity>.Filter.Eq(e => e.Id, entity.Id));
        }
    }
}
