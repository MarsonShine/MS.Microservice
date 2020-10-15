using MongoDB.Driver;
using System;
using System.Collections.Generic;

namespace MS.MicroService.MongoDb
{
    public abstract class MongoDbContext : IMongoDbContext
    {
        protected virtual IMongoDbContextModelSource MongoDbContextModelSource { get; set; } = null!;

        public IMongoDatabase Database { get; private set; } = null!;

        public virtual IMongoCollection<T> Collection<T>()
        {
            return Database.GetCollection<T>(GetCollectionName<T>());
        }

        public virtual void InitializeDatabase(IMongoDatabase database)
        {
            Database = database;
            MongoDbContextModelSource = new MongoDbContextModelSource();
        }

        protected virtual string GetCollectionName<T>()
        {
            var entityModel = this.MongoDbContextModelSource.GetModel(this).Entities.GetValueOrDefault(typeof(T));
            if (string.IsNullOrEmpty(entityModel?.CollectionName))
                throw new ArgumentNullException($"colletion of {typeof(T).FullName} not have mongodb collection name!");

            return entityModel.CollectionName;
        }
    }
}
