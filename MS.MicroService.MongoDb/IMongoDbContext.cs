using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Text;

namespace MS.MicroService.MongoDb
{
    public interface IMongoDbContext
    {
        IMongoDatabase Database { get; }

        IMongoCollection<T> Collection<T>();
    }
}
