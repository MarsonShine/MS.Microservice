using MS.MicroService.MongoDb.Log;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MS.MicroService.MongoDb.Repository
{
    public class MongoDbLogRepository : MongoDbRepository<MongoDbLogDbContext, MongoDbLogEntity>, IMongoDbLogRepository
    {
        public MongoDbLogRepository(IMongoDbContextProvider<MongoDbLogDbContext> dbContextProvider) : base(dbContextProvider)
        {
        }

        public async Task CreateAsync(MongoDbLogEntity logEntity)
        {
            await CreateAsync(logEntity);
        }
    }
}
