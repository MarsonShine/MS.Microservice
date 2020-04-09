using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MS.Microservice.MongoDb.Test.Repositoies;
using MS.MicroService.MongoDb;
using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Microservice.MongoDb.Test
{
    public class MongoDbTestBase
    {
        protected IServiceProvider RootServiceProvider { get; }
        protected IServiceCollection Services { get; }

        protected MongoDbTestBase()
        {
            var services = CreateServiceCollection();
            services.AddMongoDbService();

            services.Configure<MongoDbConnectStringOption>(option => option.MongoDbServer = "mongodb://localhost:27017");

            services.AddSingleton<ITestMongoDbContext, TestMongoDbContext>();

            RegisterRepository(services);

            Services = services;

            RootServiceProvider = CreateServiceProvider(services);
        }

        private IServiceCollection CreateServiceCollection()
        {
            return new ServiceCollection();
        }

        private IServiceProvider CreateServiceProvider(IServiceCollection services)
        {
            return services.BuildServiceProvider();
        }

        private void RegisterRepository(IServiceCollection services)
        {
            services.TryAddTransient<ICityRepository, CityRepository>();
        }
    }
}
