using Autofac;
using MS.Microservice.Domain.Identity.Token;
using MS.Microservice.Web.Application.Identity;
using MS.Microservice.Web.Application.Identity.Token;

namespace MS.Microservice.Web.AutofacModules;

public class IdentityApplicationModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<BearerTokenGenerator>()
            .As<ITokenGenerator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<SignInManager>()
            .AsSelf()
            .InstancePerDependency();
    }
}