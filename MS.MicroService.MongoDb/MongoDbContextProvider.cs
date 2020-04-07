using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MS.Microservice.Core.Data;
using System;

namespace MS.MicroService.MongoDb
{
    public class MongoDbContextProvider<TMongoDbContext> : IMongoDbContextProvider<TMongoDbContext>
        where TMongoDbContext : IMongoDbContext
    {
        private readonly string connectionString;
        private readonly IServiceProvider serviceProvider;
        public MongoDbContextProvider(
            IServiceProvider serviceProvider,
            IOptionsSnapshot<MongoDbConnectStringOption> options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            connectionString = options.Value.MongoDbServer;
            this.serviceProvider = serviceProvider;
        }

        public TMongoDbContext GetDbContext()
        {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            var mongoUrl = new MongoUrl(connectionString);
            var databaseName = mongoUrl.DatabaseName;
            // TODO：这里后续要为每一个 mongodb 连接字符串缓存一个客户端实例
            IMongoClient client = new MongoClient(mongoUrl);

            if (string.IsNullOrEmpty(databaseName))
            {
                databaseName = DatabaseNameStringAttribute.GetConnStringName<TMongoDbContext>();
            }

            var database = client.GetDatabase(databaseName);
            var dbContext = serviceProvider.GetRequiredService<TMongoDbContext>();

            dbContext.ToMongoDbContext().InitializeDatabase(database);

            return dbContext;
        }
    }
}
