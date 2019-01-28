using Autofac;
using MS.Microservice.Web.ApplicationServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MS.Microservice.Web.AutofacModules
{
    public class ApplicationAutoModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            //builder.RegisterType<IOrderService>()
            //    .As<OrderService>()
            //    .InstancePerDependency();

            builder.RegisterAssemblyTypes(typeof(IOrderService).Assembly)
                .InstancePerDependency();
        }
    }
}
