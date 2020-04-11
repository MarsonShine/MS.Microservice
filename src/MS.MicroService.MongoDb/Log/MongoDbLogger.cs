using Microsoft.Extensions.Logging;
using MS.MicroService.MongoDb.Repository;
using System;
using System.Collections.Generic;
using System.Text;

namespace MS.MicroService.MongoDb.Log
{
    public class MongoDbLogger:ILogger
    {
        private readonly string _categoryName;
        private readonly IMongoDbLogRepository _mongoDbLogRepository;

        public MongoDbLogger(string categoryName, IMongoDbLogRepository mongoDbLogRepository)
        {
            _categoryName = categoryName;
            _mongoDbLogRepository = mongoDbLogRepository;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            // TODO 这里做 db 操作
            if (state is MongoDbLogEntity)
            {
                var model = state as MongoDbLogEntity;
                model.LogDateTime = DateTime.Now;
                model.CategoryName = _categoryName;
                _mongoDbLogRepository.CreateAsync(model).ConfigureAwait(false).GetAwaiter().GetResult();
            }
        }
    }
}
