using System;

namespace MS.MicroService.MongoDb
{
    public static class IMongoDbContextExtensions
    {
        public static MongoDbContext ToMongoDbContext(this IMongoDbContext dbContext)
        {
            var mongoDbContext = dbContext as MongoDbContext;
            if (mongoDbContext == null)
                throw new ArgumentNullException($"{dbContext.GetType().AssemblyQualifiedName} 无法转换 ${typeof(MongoDbContext).AssemblyQualifiedName}");
            return mongoDbContext;
        }
    }
}
