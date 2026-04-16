using Autofac;
using MS.Microservice.Web.Application.Queries;
using MS.Microservice.Web.Application.Queries.Constract;
using MS.Microservice.Web.Application.Users;
using MS.Microservice.Web.Infrastructure.Applications.Users;
using MS.Microservice.Web.Infrastructure.Dapper;

namespace MS.Microservice.Web.AutofacModules
{
    public class AppServiceModule : Module
    {
        public ConnectionString ConnectionString { get; }
        public AppServiceModule(ConnectionString connectionString)
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

            builder.RegisterType<UserCreateAppService>()
                .As<IUserCreateAppService>()
                .InstancePerLifetimeScope();

            builder.RegisterType<UserModifyAppService>()
                .As<IUserModifyAppService>()
                .InstancePerLifetimeScope();
        }
    }
}
