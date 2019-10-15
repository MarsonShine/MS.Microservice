using Autofac;
using MS.Microservice.Web.ApplicationServices;
using System.Linq;
using MS.Microservice.Web.Domains.Repositories.Contracts;

namespace MS.Microservice.Web.AutofacModules
{
    public class ApplicationAutoModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            //接口指定服务实现类型
            //builder.RegisterType<OrderService>()
            //    .As<IOrderService>()
            //    .InstancePerDependency();

            //按照约定自动注册接口服务类型
            builder.RegisterAssemblyTypes(typeof(IOrderService).Assembly)
                .PublicOnly()
                .Where(t => t.Name.EndsWith("Service"))
                .AsImplementedInterfaces()
                .InstancePerLifetimeScope();

            builder.RegisterAssemblyTypes(typeof(IOrderRepository).Assembly)
                .PublicOnly()
                .Where(t => t.Name.EndsWith("Repository"))
                .AsImplementedInterfaces()
                .InstancePerLifetimeScope();
        }
    }
}
