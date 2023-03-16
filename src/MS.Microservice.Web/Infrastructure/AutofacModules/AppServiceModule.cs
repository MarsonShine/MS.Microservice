using Autofac;
using MS.Microservice.Web.Application.Queries;
using MS.Microservice.Web.Application.Queries.Constract;
using MS.Microservice.Web.Infrastructure.Applications.Users;

namespace MS.Microservice.Web.AutofacModules
{
    public class AppServiceModule : Module
    {
        public string ConnectionString { get; }
        public AppServiceModule(string connectionString)
        {
            ConnectionString = connectionString;
        }
        protected override void Load(ContainerBuilder builder)
        {
            //接口指定服务实现类型
            //builder.RegisterType<OrderService>()
            //    .As<IOrderService>()
            //    .InstancePerDependency();
            builder.RegisterType<CurrentUserResolver>()
                .AsSelf()
                .InstancePerLifetimeScope();

            builder.Register(c => new UserQuery(ConnectionString))
                .As<IUserQuery>()
                .InstancePerLifetimeScope();
        }
    }
}
