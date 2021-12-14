using Autofac;
using MS.Microservice.Web.AutofacModules.Extensions;

namespace MS.Microservice.Web.AutofacModules
{
    public class CommonInfrastructureModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterMediatorService();
        }
    }
}
