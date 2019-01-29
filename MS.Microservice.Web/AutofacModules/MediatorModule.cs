using Autofac;
using MediatR;
using MS.Microservice.Web.Apps.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace MS.Microservice.Web.AutofacModules
{
    public class MediatorModule : Autofac.Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder
                .RegisterType<Mediator>()
                .As<IMediator>()
                .InstancePerLifetimeScope();

            builder.RegisterAssemblyTypes(typeof(CreateOrderHandler).GetTypeInfo().Assembly).AsImplementedInterfaces();// or
            //builder.RegisterType<CreateOrderHandler>().AsImplementedInterfaces().InstancePerDependency();

            builder.Register<ServiceFactory>(context =>
            {
                var c = context.Resolve<IComponentContext>();
                return t => c.Resolve(t);
            });
        }
    }
}
