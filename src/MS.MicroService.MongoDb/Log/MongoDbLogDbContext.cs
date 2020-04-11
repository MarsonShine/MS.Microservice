using MongoDB.Driver;
using MS.Microservice.Core.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace MS.MicroService.MongoDb.Log
{
    [DatabaseNameString("BBSSPlatform")]
    public class MongoDbLogDbContext : MongoDbContext
    {
        [MongoCollection("Logs")]
        public IMongoCollection<MongoDbLogEntity> People => Collection<MongoDbLogEntity>();
    }
}
