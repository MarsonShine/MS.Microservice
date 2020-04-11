using MS.MicroService.MongoDb.Log;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MS.MicroService.MongoDb.Repository
{
    public interface IMongoDbLogRepository
    {
        Task CreateAsync(MongoDbLogEntity logEntity);
    }
}
