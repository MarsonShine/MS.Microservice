using Microsoft.Extensions.Logging;
using MS.MicroService.MongoDb.Repository;

namespace MS.MicroService.MongoDb.Log
{
    public class MongoDbLoggerProvider : ILoggerProvider
    {
        private readonly IMongoDbLogRepository _mongoDbLogRepository;
        public MongoDbLoggerProvider(IMongoDbLogRepository mongoDbLogRepository)
        {
            _mongoDbLogRepository = mongoDbLogRepository;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new MongoDbLogger(categoryName, _mongoDbLogRepository);
        }

        public void Dispose()
        {

        }
    }
}
