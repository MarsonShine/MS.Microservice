using Autofac;
using Microsoft.AspNetCore.Identity;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Identity.Token;
using MS.Microservice.Lab.Application.Identity;
using MS.Microservice.Lab.Application.Identity.Token;

namespace MS.Microservice.Lab.AutofacModules;

public class IdentityApplicationModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<PasswordHasher<User>>()
            .As<IPasswordHasher<User>>()
            .SingleInstance();

        builder.RegisterType<UserPasswordService>()
            .As<IUserPasswordService>()
            .InstancePerLifetimeScope();

        builder.RegisterType<BearerTokenGenerator>()
            .As<ITokenGenerator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<SignInManager>()
            .AsSelf()
            .InstancePerDependency();
    }
}
