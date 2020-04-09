using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MS.MicroService.MongoDb
{
    public static class IServiceCollectionExtensions
    {
        public static void AddMongoDbService(this IServiceCollection services)
        {
            services.TryAddTransient(typeof(IMongoDbContextProvider<>), typeof(MongoDbContextProvider<>));
        }
    }
}
