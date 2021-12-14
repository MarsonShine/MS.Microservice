using Autofac;
using MS.Microservice.Web.Application.Commands;
using MediatR;
using MediatR.Pipeline;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace MS.Microservice.Web.AutofacModules.Extensions
{
    public static class AutofacModuleExtensions
    {
        public static void RegisterMediatorService(this ContainerBuilder builder)
        {
            builder.RegisterAssemblyTypes(typeof(IMediator).GetTypeInfo().Assembly).AsImplementedInterfaces();

            // 自动注册
            //builder.RegisterAssemblyTypes(typeof(UserCreatedCommand).GetTypeInfo().Assembly).AsImplementedInterfaces();// or
            //builder.RegisterType<UserCreatedCommandHandler>().AsImplementedInterfaces().InstancePerDependency();
            //builder.RegisterAssemblyTypes(typeof(IRequestHandler<,>).GetTypeInfo().Assembly)

            var openHandlerTypes = new[]
            {
                //typeof(IRequestPreProcessor<>),
                typeof(IRequestHandler<,>),
                //typeof(IRequestPostProcessor<,>),
                //typeof(IRequestExceptionHandler<,,>),
                //typeof(IRequestExceptionAction<,>),
                typeof(INotificationHandler<>),
            };

            foreach (var openHandlerType in openHandlerTypes)
            {
                builder.RegisterAssemblyTypes(typeof(UserCreatedCommand).GetTypeInfo().Assembly)
                    .AsClosedTypesOf(openHandlerType);
            }

            builder.RegisterGeneric(typeof(RequestPostProcessorBehavior<,>)).As(typeof(IPipelineBehavior<,>));
            builder.RegisterGeneric(typeof(RequestPreProcessorBehavior<,>)).As(typeof(IPipelineBehavior<,>));

            //builder.RegisterGeneric(typeof(RequestExceptionActionProcessorBehavior<,>)).As(typeof(IPipelineBehavior<,>));
            //builder.RegisterGeneric(typeof(RequestExceptionProcessorBehavior<,>)).As(typeof(IPipelineBehavior<,>));

            //foreach (var customBehaviorType in this.customBehaviorTypes)
            //{
            //    builder.RegisterGeneric(customBehaviorType)
            //        .As(typeof(IPipelineBehavior<,>));
            //}

            builder.Register<ServiceFactory>(outerContext =>
            {
                var innerContext = outerContext.Resolve<IComponentContext>();

                return serviceType => innerContext.Resolve(serviceType);
            });
        }


        public static void RegisterPlatformAutofacModule(this ContainerBuilder builder,IConfiguration configuration)
        {
            builder
                .RegisterModule<CommonInfrastructureModule>()
                .RegisterModule<DomainServiceModule>()
                .RegisterModule(new AppServiceModule(configuration.GetConnectionString("ActivationReaderConnection")))
                ;
        }
    }
}
