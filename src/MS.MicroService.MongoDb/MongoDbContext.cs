using MongoDB.Driver;
using System;

namespace MS.MicroService.MongoDb
{
    public abstract class MongoDbContext : IMongoDbContext
    {
        public IMongoDatabase Database { get; private set; }

        public IMongoCollection<T> Collection<T>()
        {
            throw new NotImplementedException();
        }

        public virtual void InitializeDatabase(IMongoDatabase database)
        {
            Database = database;
        }
    }
}
