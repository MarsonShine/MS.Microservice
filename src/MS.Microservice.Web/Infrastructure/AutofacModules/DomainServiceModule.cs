using Autofac;
using MS.Microservice.Domain.Aggregates.IdentityModel.Repository;
using MS.Microservice.Domain.Aggregates.LogAggregate.Repository;
using MS.Microservice.Domain.Identity;
using MS.Microservice.Domain.Identity.Token;
using MS.Microservice.Domain.Services;
using MS.Microservice.Domain.Services.Interfaces;
using MS.Microservice.Infrastructure.Repository;

namespace MS.Microservice.Web.AutofacModules
{
    public class DomainServiceModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            //接口指定服务实现类型
            builder.RegisterType<BearerTokenGenerator>()
                .As<ITokenGenerator>()
                .InstancePerLifetimeScope();

            builder.RegisterType<SignInManager>()
                .AsSelf()
                .InstancePerDependency();

            // 仓储
            builder.RegisterType<UserRepository>()
                .As<IUserRepository>()
                .InstancePerDependency();
           
            builder.RegisterType<LogRepository>()
                .As<ILogRepository>()
                .InstancePerDependency();


            // 领域服务
            builder.RegisterType<UserDomainService>()
                .As<IUserDomainService>()
                .InstancePerDependency();
        }
    }
}
