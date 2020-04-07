using System;
using System.Collections.Generic;
using System.Text;

namespace MS.MicroService.MongoDb
{
    public interface IMongoDbContextProvider<out TMongoDbContext>
        where TMongoDbContext : IMongoDbContext
    {
        TMongoDbContext GetDbContext();
    }
}
